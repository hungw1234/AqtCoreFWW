using System.Text.Json;
using AqtCoreFW.BusinessProcess.Application.DTOs;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Queries.GetBpRunSubmissions;

/// <summary>
/// Assemble the latest submitted form data for a BP-Run into render-ready rows (label + value).
/// Mirrors the prefill resolver's data gathering: GetLatestForRunAsync (one submission per form) →
/// GetFullSubmissionAsync (values) → GetVersionSchemaByPublicIdAsync (labels from the pinned snapshot).
///
/// Label resolution stays server-side so the operational page never touches the Forms-admin schema
/// endpoints — the same separation that keeps this read on the BP.View permission boundary.
/// </summary>
public sealed class GetBpRunSubmissionsQueryHandler(
    ITenantContext tenantContext,
    IFormSubmissionRunClient submissionRunClient,
    IFormTemplateQueryClient formTemplateClient,
    ILogger<GetBpRunSubmissionsQueryHandler> logger)
    : IRequestHandler<GetBpRunSubmissionsQuery, Result<IReadOnlyList<BpRunSubmissionDto>>>
{
    public async Task<Result<IReadOnlyList<BpRunSubmissionDto>>> Handle(
        GetBpRunSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant GetBpRunSubmissions denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        // Latest submission per form in the run (intake + state forms). Unknown/cross-tenant run →
        // empty per IFormSubmissionRunClient contract (does not leak existence) → empty result.
        var summaries = await submissionRunClient.GetLatestForRunAsync(
            request.WorkflowInstanceId, request.TenantId, cancellationToken);

        if (summaries.Count == 0)
            return Result.Ok<IReadOnlyList<BpRunSubmissionDto>>(Array.Empty<BpRunSubmissionDto>());

        var result = new List<BpRunSubmissionDto>(summaries.Count);

        // Intake (WorkflowStateId null) first, then by submission time — matches the run chronology.
        foreach (var summary in summaries.OrderBy(s => s.WorkflowStateId.HasValue).ThenBy(s => s.SubmittedAt))
        {
            var data = await submissionRunClient.GetFullSubmissionAsync(
                           summary.Id, request.TenantId, cancellationToken)
                       ?? new Dictionary<string, object?>();

            var snapshot = await formTemplateClient.GetVersionSchemaByPublicIdAsync(
                summary.FormTemplatePublicId, summary.FormTemplateVersion, request.TenantId, cancellationToken);

            if (snapshot is null)
            {
                // Submission exists but its pinned snapshot is gone (data-integrity). Skip rather than
                // fabricate labels — the other forms still render.
                logger.LogWarning(
                    "BP-Run {RunId}: submission {SubId} pins snapshot {FormId} v{Version} which is missing — skipped.",
                    request.WorkflowInstanceId, summary.Id, summary.FormTemplatePublicId, summary.FormTemplateVersion);
                continue;
            }

            result.Add(new BpRunSubmissionDto(
                summary.FormTemplatePublicId,
                summary.FormTemplateVersion,
                summary.WorkflowStateId,
                summary.SubmittedAt,
                BuildFields(snapshot.SchemaJson, data, summary.FormTemplatePublicId),
                IsSigned: summary.SignatureVerdict == true,
                SignerName: ExtractCommonName(summary.SignerSubject),
                SignatureVerifiedAt: summary.SignatureVerifiedAt));
        }

        return Result.Ok<IReadOnlyList<BpRunSubmissionDto>>(result);
    }

    /// <summary>
    /// Pair each snapshot field (in DisplayOrder) with its submitted value. The snapshot is the label
    /// authority — keys present in the data but absent from the snapshot are dropped, so a field removed
    /// in a newer template version does not surface as a label-less raw key.
    /// </summary>
    private IReadOnlyList<BpRunSubmissionFieldDto> BuildFields(
        string schemaJson,
        IReadOnlyDictionary<string, object?> data,
        Guid formTemplatePublicId)
    {
        SchemaSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SchemaSnapshot>(
                schemaJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "GetBpRunSubmissions: malformed snapshot JSON for form {FormId} — returning no fields.",
                formTemplatePublicId);
            return Array.Empty<BpRunSubmissionFieldDto>();
        }

        if (snapshot?.Fields is null || snapshot.Fields.Count == 0)
            return Array.Empty<BpRunSubmissionFieldDto>();

        return snapshot.Fields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new BpRunSubmissionFieldDto(
                f.Key,
                f.Type ?? string.Empty,
                string.IsNullOrWhiteSpace(f.LabelVi) ? f.Key : f.LabelVi!,
                string.IsNullOrWhiteSpace(f.LabelEn) ? f.Key : f.LabelEn!,
                data.TryGetValue(f.Key, out var v) ? v : null))
            .ToList();
    }

    // Extract the certificate Common Name from a subject DN for display. The full DN carries the
    // signer's national ID (CCCD) and locality — kept server-side; only the human name is surfaced.
    private static string? ExtractCommonName(string? subjectDn)
    {
        if (string.IsNullOrWhiteSpace(subjectDn))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            subjectDn, @"CN=([^,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    // Snapshot envelope — only the keys we need; unknown keys ignored (forward-compatible).
    // Aligns with FormTemplateVersionFactory.FieldPayload (Key, Type, LabelVi, LabelEn, DisplayOrder).
    // Type is the FormFieldType name; tolerated as null for snapshots predating the field (coerced to "").
    private sealed record SchemaSnapshot(List<SnapshotField>? Fields);
    private sealed record SnapshotField(string Key, string? Type, string? LabelVi, string? LabelEn, int DisplayOrder);
}
