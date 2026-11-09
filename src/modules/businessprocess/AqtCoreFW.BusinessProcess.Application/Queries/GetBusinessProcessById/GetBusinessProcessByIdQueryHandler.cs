using AqtCoreFW.BusinessProcess.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Queries.GetBusinessProcessById;

/// <summary>
/// Resolves BP root by PublicId, then sub-queries children by internal bigint Id to avoid second AK lookup.
/// Children carry duplicated TenantId (defense-in-depth) — filter explicit on every read.
/// P7-FU1.2: also resolves WorkflowDefinitionPublicId via IWorkflowModuleClient so FE form-map
/// designer can call the existing workflow detail endpoint without leaking the long bigint FK.
/// </summary>
public sealed class GetBusinessProcessByIdQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    IWorkflowModuleClient workflowClient,
    IFormTemplateQueryClient formTemplateClient,
    ILogger<GetBusinessProcessByIdQueryHandler> logger)
    : IRequestHandler<GetBusinessProcessByIdQuery, Result<BusinessProcessDetailDto>>
{
    public async Task<Result<BusinessProcessDetailDto>> Handle(
        GetBusinessProcessByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant GetBusinessProcessById denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        const string rootSql = """
            SELECT Id AS InternalId, PublicId AS Id, Code, Name, Description,
                   WorkflowDefinitionId, IntakeFormTemplateId, IntakeFormTemplateVersion,
                   IsActive, CreatedAt, UpdatedAt, RowVersion
            FROM businessprocess.BusinessProcessDefinitions
            WHERE PublicId = @PublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        const string stateFormsSql = """
            SELECT PublicId AS Id, WorkflowStateId, FormTemplateId, FormTemplateVersion, IsRequired, DisplayOrder
            FROM businessprocess.BusinessProcessStateForms
            WHERE BusinessProcessDefinitionId = @InternalId AND TenantId = @TenantId AND IsDeleted = 0
            ORDER BY DisplayOrder, WorkflowStateId
            """;

        const string transitionFormsSql = """
            SELECT PublicId AS Id, WorkflowTransitionId, FormTemplateId, FormTemplateVersion, IsRequired, DisplayOrder
            FROM businessprocess.BusinessProcessTransitionForms
            WHERE BusinessProcessDefinitionId = @InternalId AND TenantId = @TenantId AND IsDeleted = 0
            ORDER BY DisplayOrder, WorkflowTransitionId
            """;

        var rootArgs = new { PublicId = request.BusinessProcessPublicId, request.TenantId };
        var root = await db.QueryFirstOrDefaultAsync<BusinessProcessRootRow>(rootSql, rootArgs, cancellationToken);

        if (root is null)
            return Result.Fail(new NotFoundError($"BusinessProcess {request.BusinessProcessPublicId} không tìm thấy."));

        var childArgs = new { InternalId = root.InternalId, request.TenantId };
        var stateForms = (await db.QueryAsync<BusinessProcessStateFormDto>(stateFormsSql, childArgs, cancellationToken)).ToList();
        var transitionForms = (await db.QueryAsync<BusinessProcessTransitionFormDto>(transitionFormsSql, childArgs, cancellationToken)).ToList();

        // BUG2 fix 2026-05-18: resolve FormTemplate internal bigint FK → Guid PublicId server-side
        // so the FE picker binds directly (the picker DTO carries only the Guid — the FE has no
        // bigint↔Guid bridge). Mirrors the WorkflowDefinitionPublicId / IWorkflowModuleClient
        // pattern below. Batched (one round-trip) to avoid N+1 across all state/transition forms.
        var formTemplateIds = stateForms.Select(f => f.FormTemplateId)
            .Concat(transitionForms.Select(f => f.FormTemplateId))
            .Distinct()
            .ToArray();
        var formTemplatePublicIds = await formTemplateClient.ResolveTemplatePublicIdsAsync(
            formTemplateIds, request.TenantId, cancellationToken);

        stateForms = stateForms
            .Select(f => formTemplatePublicIds.TryGetValue(f.FormTemplateId, out var pid)
                ? f with { FormTemplatePublicId = pid }
                : f)
            .ToList();
        transitionForms = transitionForms
            .Select(f => formTemplatePublicIds.TryGetValue(f.FormTemplateId, out var pid)
                ? f with { FormTemplatePublicId = pid }
                : f)
            .ToList();

        // RowVersion is a SQL rowversion (byte[8]); encode to base64 for JSON transport.
        // FE sends this verbatim as If-Match header to the ReplaceFormMaps endpoint.
        var rowVersionBase64 = Convert.ToBase64String(root.RowVersion);

        // P7-FU1.2: resolve WorkflowDefinition Guid AK via cross-module client (no cross-schema SQL).
        // Null tolerated: orphan FK (workflow hard-deleted) lets the BP detail page still render.
        var workflowPublicId = await workflowClient.TryGetDefinitionPublicIdAsync(
            root.WorkflowDefinitionId, request.TenantId, cancellationToken);

        if (workflowPublicId is null)
        {
            logger.LogWarning(
                "BusinessProcess {BusinessProcessId} references missing/cross-tenant WorkflowDefinitionId={WorkflowDefinitionId} for tenant {TenantId}",
                root.Id, root.WorkflowDefinitionId, request.TenantId);
        }

        return Result.Ok(new BusinessProcessDetailDto(
            root.Id, root.Code, root.Name, root.Description,
            root.WorkflowDefinitionId, workflowPublicId, root.IntakeFormTemplateId, root.IntakeFormTemplateVersion,
            root.IsActive, root.CreatedAt, root.UpdatedAt,
            stateForms, transitionForms, rowVersionBase64));
    }
}

/// <summary>
/// Dapper projection row for the BP root SELECT. Namespace-scoped (not nested) so
/// unit tests can mock <c>IReadDbConnection.QueryFirstOrDefaultAsync&lt;BusinessProcessRootRow&gt;</c>
/// — mirrors the <c>GetIntakeFormSchemaRow</c> precedent (P4 Slice 2B).
/// </summary>
public sealed class BusinessProcessRootRow
{
    public long InternalId { get; set; }
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long WorkflowDefinitionId { get; set; }
    public long? IntakeFormTemplateId { get; set; }
    public int? IntakeFormTemplateVersion { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
