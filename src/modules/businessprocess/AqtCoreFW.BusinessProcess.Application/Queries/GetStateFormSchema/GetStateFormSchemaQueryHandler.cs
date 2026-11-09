using AqtCoreFW.BusinessProcess.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Queries.GetStateFormSchema;

/// <summary>
/// P8-FU1: resolve a state-form map's pinned FormTemplate snapshot. Three-step:
///   1. Pattern-A tenant guard pre-DB.
///   2. Dapper read on businessprocess.BusinessProcessStateForms JOIN BusinessProcessDefinitions
///      same-schema — get internal FormTemplateId + pinned version. NotFound on miss/cross-tenant.
///   3. Delegate to <see cref="IFormTemplateQueryClient"/> for the snapshot — no cross-schema SQL.
///
/// SystemAdmin bypass on reads (mirrors GetIntakeFormSchema / GetBusinessProcessById).
/// </summary>
public sealed class GetStateFormSchemaQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    IFormTemplateQueryClient formTemplateClient,
    ILogger<GetStateFormSchemaQueryHandler> logger)
    : IRequestHandler<GetStateFormSchemaQuery, Result<BusinessProcessIntakeFormDto>>
{
    public async Task<Result<BusinessProcessIntakeFormDto>> Handle(
        GetStateFormSchemaQuery request,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant GetStateFormSchema denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        // Same-schema join: state-form-map must belong to the (BP, tenant) named in the URL —
        // bp PublicId is part of the WHERE so a leaked map Guid from another BP returns NotFound
        // rather than silently rendering on a sibling BP context.
        const string sql = """
            SELECT sf.FormTemplateId, sf.FormTemplateVersion
            FROM businessprocess.BusinessProcessStateForms sf
            INNER JOIN businessprocess.BusinessProcessDefinitions bp
                ON bp.Id = sf.BusinessProcessDefinitionId AND bp.TenantId = sf.TenantId
            WHERE sf.PublicId = @StateFormMapPublicId
              AND bp.PublicId = @BusinessProcessPublicId
              AND sf.TenantId = @TenantId
              AND sf.IsDeleted = 0
              AND bp.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<GetStateFormSchemaRow>(
            sql,
            new
            {
                request.StateFormMapPublicId,
                request.BusinessProcessPublicId,
                request.TenantId,
            },
            cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"State-form map {request.StateFormMapPublicId} không tìm thấy."));

        var snapshot = await formTemplateClient.GetVersionSchemaByInternalIdAsync(
            row.FormTemplateId,
            row.FormTemplateVersion,
            request.TenantId,
            cancellationToken);

        if (snapshot is null)
        {
            // Pinned FK exists but snapshot is missing — data-integrity bug (template hard-deleted
            // without R5 guard, or version row dropped). Surface NotFound; FE shows a clear error
            // instead of "no fields" empty render.
            logger.LogWarning(
                "BP {BpPublicId} state-form-map {MapPublicId} pins FormTemplate {TemplateId} v{Version} but snapshot is missing.",
                request.BusinessProcessPublicId,
                request.StateFormMapPublicId,
                row.FormTemplateId,
                row.FormTemplateVersion);
            return Result.Fail(new NotFoundError(
                $"State-form snapshot v{row.FormTemplateVersion} không tìm thấy."));
        }

        return Result.Ok(new BusinessProcessIntakeFormDto(
            snapshot.FormTemplatePublicId,
            snapshot.VersionNumber,
            snapshot.SchemaJson));
    }
}

/// <summary>
/// Dapper projection row for the state-form-map SELECT. Namespace-scoped (not nested) so unit
/// tests can mock <c>IReadDbConnection.QueryFirstOrDefaultAsync&lt;GetStateFormSchemaRow&gt;</c>
/// — mirrors the <c>GetIntakeFormSchemaRow</c> precedent (P4 Slice 2B).
/// </summary>
public sealed class GetStateFormSchemaRow
{
    public long FormTemplateId { get; set; }
    public int FormTemplateVersion { get; set; }
}
