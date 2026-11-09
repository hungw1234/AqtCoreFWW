using AqtCoreFW.BusinessProcess.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Queries.GetIntakeFormSchema;

/// <summary>
/// Resolves the intake-form snapshot for a BP definition. Two-step:
///   1. Local Dapper read on businessprocess.BusinessProcessDefinitions to get the intake bigint FK + pinned version.
///   2. Delegate to IFormTemplateQueryClient (Forms cross-module client) — no cross-schema SQL.
///
/// Pattern A (R13) pre-DB tenant guard. SystemAdmin bypass on reads (mirrors GetBusinessProcessById).
/// </summary>
public sealed class GetIntakeFormSchemaQueryHandler(
    IReadDbConnection db,
    ITenantContext tenantContext,
    IFormTemplateQueryClient formTemplateClient,
    ILogger<GetIntakeFormSchemaQueryHandler> logger)
    : IRequestHandler<GetIntakeFormSchemaQuery, Result<BusinessProcessIntakeFormDto?>>
{
    public async Task<Result<BusinessProcessIntakeFormDto?>> Handle(
        GetIntakeFormSchemaQuery request,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.IsSystemAdmin && request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant GetIntakeFormSchema denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        const string sql = """
            SELECT IntakeFormTemplateId, IntakeFormTemplateVersion
            FROM businessprocess.BusinessProcessDefinitions
            WHERE PublicId = @PublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<GetIntakeFormSchemaRow>(
            sql,
            new { PublicId = request.BusinessProcessPublicId, request.TenantId },
            cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"BusinessProcess {request.BusinessProcessPublicId} không tìm thấy."));

        // GOV_BP_010 invariant: both null OR both set. Aggregate factory enforces — read-side trusts that.
        if (row.IntakeFormTemplateId is null || row.IntakeFormTemplateVersion is null)
            return Result.Ok<BusinessProcessIntakeFormDto?>(null);

        var snapshot = await formTemplateClient.GetVersionSchemaByInternalIdAsync(
            row.IntakeFormTemplateId.Value,
            row.IntakeFormTemplateVersion.Value,
            request.TenantId,
            cancellationToken);

        if (snapshot is null)
        {
            // Pinned FK exists but the snapshot row is missing — data-integrity bug (template deleted
            // without R5 guard, or version row dropped). Surface as NotFound so the FE shows a clear
            // error rather than a silent "no intake form" 204.
            logger.LogWarning(
                "BP {BpPublicId} pins FormTemplate {TemplateId} v{Version} but snapshot is missing.",
                request.BusinessProcessPublicId,
                row.IntakeFormTemplateId.Value,
                row.IntakeFormTemplateVersion.Value);
            return Result.Fail(new NotFoundError(
                $"Intake form snapshot v{row.IntakeFormTemplateVersion.Value} không tìm thấy."));
        }

        return Result.Ok<BusinessProcessIntakeFormDto?>(new BusinessProcessIntakeFormDto(
            snapshot.FormTemplatePublicId,
            snapshot.VersionNumber,
            snapshot.SchemaJson));
    }

    // Dapper requires a parameterless ctor + settable props — positional records fail materialization
    // on nullable cols. Exposed at namespace scope so tests can configure NSubstitute returns by
    // generic-type binding (avoiding private-nested-type capture).
}

public sealed class GetIntakeFormSchemaRow
{
    public long? IntakeFormTemplateId { get; set; }
    public int? IntakeFormTemplateVersion { get; set; }
}
