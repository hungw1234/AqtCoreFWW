using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AqtCoreFW.Forms.Infrastructure.Clients;

/// <summary>
/// In-process implementation of IFormSubmissionRunClient (P2).
/// - Read methods: Dapper via IReadDbConnection (mirrors GetFormSubmissionQueryHandler pattern;
///   IFormSubmissionRepository is write-path only per its doc-comment).
/// - Write method: delegates to SubmitFormCommand via IMediator after enforcing R14
///   cross-tenant guard against workflow.WorkflowInstances.TenantId. Cross-module DB read
///   is acceptable in monolith for FK validation; will become a Workflow client call when
///   the module is extracted.
///
/// Tenant scoping: every read filters by TenantId in WHERE; cross-tenant queries return
/// null/empty (do not leak existence). Write path throws GOV_BP_002 on tenant mismatch.
/// </summary>
public sealed class InProcessFormSubmissionRunClient(
    IReadDbConnection db,
    IMediator mediator,
    IWorkflowModuleClient workflowClient,
    IFormDataStorageFactory storageFactory,
    ILogger<InProcessFormSubmissionRunClient> logger)
    : IFormSubmissionRunClient
{
    public async Task<IReadOnlyList<FormSubmissionSummaryDto>> GetLatestForRunAsync(
        Guid workflowInstanceId, Guid tenantId, CancellationToken ct = default)
    {
        // One row per FormTemplateId — most recent SubmittedAt wins. P6 prefill consumer.
        // JOIN forms.FormTemplates to project t.PublicId (T2 AK) — Phase-07 O-2 pattern.
        const string sql = """
            WITH ranked AS (
                SELECT s.PublicId, s.TenantId, t.PublicId AS FormTemplatePublicId,
                       s.FormTemplateVersion, s.WorkflowInstanceId, s.WorkflowStateId,
                       s.SubmittedAt, s.SubmittedBy, s.Status,
                       s.SignatureVerdict, s.SignerSubject, s.SignatureVerifiedAt,
                       ROW_NUMBER() OVER (
                           PARTITION BY s.FormTemplateId
                           ORDER BY s.SubmittedAt DESC
                       ) AS rn
                FROM forms.FormSubmissions s
                INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
                WHERE s.WorkflowInstanceId = @WorkflowInstanceId
                  AND s.TenantId = @TenantId
                  AND s.IsDeleted = 0
            )
            SELECT PublicId AS Id, TenantId, FormTemplatePublicId, FormTemplateVersion,
                   WorkflowInstanceId, WorkflowStateId, SubmittedAt, SubmittedBy, Status,
                   SignatureVerdict, SignerSubject, SignatureVerifiedAt
            FROM ranked
            WHERE rn = 1
            ORDER BY SubmittedAt DESC
            """;

        var rows = await db.QueryAsync<FormSubmissionSummaryDto>(
            sql, new { WorkflowInstanceId = workflowInstanceId, TenantId = tenantId }, ct);
        return rows.ToList();
    }

    public async Task<FormSubmissionSummaryDto?> GetLatestStateFormAsync(
        Guid workflowInstanceId, long workflowStateId, long formTemplateId,
        Guid tenantId, CancellationToken ct = default)
    {
        // Hits IX_FormSubmissions_LatestStateForm covering index + filter.
        const string sql = """
            SELECT TOP 1
                   s.PublicId AS Id, s.TenantId, t.PublicId AS FormTemplatePublicId,
                   s.FormTemplateVersion, s.WorkflowInstanceId, s.WorkflowStateId,
                   s.SubmittedAt, s.SubmittedBy, s.Status
            FROM forms.FormSubmissions s
            INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
            WHERE s.WorkflowInstanceId = @WorkflowInstanceId
              AND s.WorkflowStateId = @WorkflowStateId
              AND s.FormTemplateId = @FormTemplateId
              AND s.TenantId = @TenantId
              AND s.IsDeleted = 0
            ORDER BY s.SubmittedAt DESC
            """;

        return await db.QueryFirstOrDefaultAsync<FormSubmissionSummaryDto>(
            sql,
            new
            {
                WorkflowInstanceId = workflowInstanceId,
                WorkflowStateId = workflowStateId,
                FormTemplateId = formTemplateId,
                TenantId = tenantId
            },
            ct);
    }

    public async Task<BpRunSubmissionOutcome> CreateForBpRunAsync(
        Guid workflowInstanceId, long? workflowStateId, Guid formTemplatePublicId,
        Guid submittedBy, IReadOnlyDictionary<string, object?> data,
        bool consentGiven, Guid tenantId, BpRunSignaturePayload? signature = null,
        CancellationToken ct = default)
    {
        // R14: load WorkflowInstance.TenantId via IWorkflowModuleClient (no cross-schema SQL —
        // module isolation guard). Returns null if instance not found. Instance-not-found and
        // tenant-mismatch are both reported as GOV_BP_002 so the caller cannot distinguish a
        // wrong-tenant run from a non-existent one (does not leak existence).
        var instanceTenant = await workflowClient.GetTenantForInstanceAsync(workflowInstanceId, ct);

        if (instanceTenant is null)
        {
            logger.LogWarning(
                "BP-Run create rejected: WorkflowInstance {InstanceId} not found.",
                workflowInstanceId);
            return BpRunSubmissionOutcome.CrossTenant("GOV_BP_002");
        }

        if (instanceTenant.Value != tenantId)
        {
            logger.LogWarning(
                "BP-Run create rejected: tenant mismatch. Instance tenant {InstanceTenant} != caller {CallerTenant}.",
                instanceTenant.Value, tenantId);
            return BpRunSubmissionOutcome.CrossTenant("GOV_BP_002");
        }

        // Map the transport-neutral carrier to the Forms.Application proof bundle here — the single
        // point where a contracts-asm type crosses into an application type (BusinessProcess never
        // sees SignaturePayload, preserving module isolation).
        var signaturePayload = signature is null
            ? null
            : new SignaturePayload(
                signature.Signature,
                signature.SignerCertDer,
                signature.Format,
                signature.HashAlg,
                signature.Encode,
                signature.ProviderKey);

        // Delegate to SubmitFormCommand — preserves validation, consent recording, storage routing,
        // signature verify-at-ingest, event publishing. The handler branches on WorkflowInstanceId to
        // skip ApprovalWorkflow instance creation (BP instance pre-exists).
        var command = new SubmitFormCommand(
            tenantId,
            formTemplatePublicId,
            submittedBy,
            data.ToDictionary(kv => kv.Key, kv => kv.Value),
            ConsentGiven: consentGiven,
            WorkflowInstanceId: workflowInstanceId,
            WorkflowStateId: workflowStateId,
            Signature: signaturePayload);

        var result = await mediator.Send(command, ct);
        if (result.IsFailed)
        {
            // Project the inner FluentResults errors into transport-neutral field errors. Validation
            // errors carry the FormField key in "Property" metadata; non-field failures fall back to
            // an empty Field. All errors are kept so the BP layer can surface every failing field.
            var fieldErrors = result.Errors
                .Select(e => new BpRunFieldError(
                    e.Metadata.TryGetValue("Property", out var prop) ? prop?.ToString() ?? string.Empty : string.Empty,
                    e.Message))
                .ToList();
            return BpRunSubmissionOutcome.SubmissionFailed(fieldErrors);
        }
        return BpRunSubmissionOutcome.Success(result.Value);
    }

    public async Task<IReadOnlyList<FormSubmissionSummaryDto>> GetAllForRunAsync(
        Guid workflowInstanceId, Guid tenantId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT s.PublicId AS Id, s.TenantId, t.PublicId AS FormTemplatePublicId,
                   s.FormTemplateVersion, s.WorkflowInstanceId, s.WorkflowStateId,
                   s.SubmittedAt, s.SubmittedBy, s.Status
            FROM forms.FormSubmissions s
            INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
            WHERE s.WorkflowInstanceId = @WorkflowInstanceId
              AND s.TenantId = @TenantId
              AND s.IsDeleted = 0
            ORDER BY s.SubmittedAt DESC
            """;

        var rows = await db.QueryAsync<FormSubmissionSummaryDto>(
            sql, new { WorkflowInstanceId = workflowInstanceId, TenantId = tenantId }, ct);
        return rows.ToList();
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetFullSubmissionAsync(
        Guid submissionPublicId, Guid tenantId, CancellationToken ct = default)
    {
        // Defense-in-depth tenant filter — cross-tenant returns null so the caller cannot infer existence.
        // StorageMode + internal FormTemplateId projected so we can route to the right backend.
        const string sql = """
            SELECT s.FormTemplateId, t.StorageMode, s.SubmissionData
            FROM forms.FormSubmissions s
            INNER JOIN forms.FormTemplates t ON t.Id = s.FormTemplateId
            WHERE s.PublicId = @SubmissionId
              AND s.TenantId = @TenantId
              AND s.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<FullSubmissionRow>(
            sql, new { SubmissionId = submissionPublicId, TenantId = tenantId }, ct);

        if (row is null) return null;

        if (Enum.TryParse<StorageMode>(row.StorageMode, out var mode) && mode == StorageMode.Table)
        {
            var storage = storageFactory.Create(StorageMode.Table);
            var data = await storage.ReadAsync(row.FormTemplateId, submissionPublicId, ct);
            // TableField nested rows intentionally NOT attached — prefill is a single-value lookup per FieldKey.
            return data is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(data);
        }

        if (string.IsNullOrWhiteSpace(row.SubmissionData))
            return new Dictionary<string, object?>();

        try
        {
            // P6-FU2: Deserialize into `Dictionary<string, object?>` boxes values as JsonElement.
            // Normalize at the boundary so consumers see uniform native CLR types regardless of
            // StorageMode (Json vs Table). INVALIDATES D9 ("no coercion") from BP MVP P6 plan.
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.SubmissionData);
            return parsed is null
                ? new Dictionary<string, object?>()
                : JsonValueNormalizer.NormalizeDict(parsed);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "GetFullSubmissionAsync: malformed SubmissionData JSON for {SubmissionId} — returning empty.",
                submissionPublicId);
            return new Dictionary<string, object?>();
        }
    }

    // arch:suppress R_T2FK FormTemplate: private inner Dapper materializer — internal long PK projection
    private sealed class FullSubmissionRow
    {
        public long FormTemplateId { get; set; }
        public string StorageMode { get; set; } = string.Empty;
        public string? SubmissionData { get; set; }
    }
}
