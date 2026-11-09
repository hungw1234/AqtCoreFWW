using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.Analytics;

/// <summary>
/// Returns active instances stuck in a state longer than MinHoursInState.
/// Uses SYSDATETIMEOFFSET() for timezone-aware diff.
/// Tenant scoped via wi.TenantId. Only active (non-completed) instances.
/// </summary>
public sealed record GetAgingReportQuery(
    Guid TenantId,
    int MinHoursInState,
    Guid? DefinitionId = null) : IRequest<Result<IReadOnlyList<AgingInstanceDto>>>;

public sealed class GetAgingReportQueryHandler(
    IReadDbConnection db,
    ICacheService cache)
    : IRequestHandler<GetAgingReportQuery, Result<IReadOnlyList<AgingInstanceDto>>>
{
    public async Task<Result<IReadOnlyList<AgingInstanceDto>>> Handle(
        GetAgingReportQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"wf:analytics:{request.TenantId}:{request.DefinitionId ?? Guid.Empty}:aging:{request.MinHoursInState}h";
        var cached = await cache.GetAsync<List<AgingInstanceDto>>(cacheKey, cancellationToken);
        if (cached != null)
            return Result.Ok<IReadOnlyList<AgingInstanceDto>>(cached);

        // T2 WorkflowDefinition: optional DefinitionId filter is Guid (PublicId), wi.DefinitionId is bigint FK.
        // AgingInstanceDto.DefinitionId = Guid (PublicId) — must project wd.PublicId, not wi.DefinitionId (bigint).
        // AgingInstanceDto.InstanceId = Guid (PublicId) — must project wi.PublicId, not wi.Id (bigint).
        // Always JOIN wd: needed for DefinitionId projection even when no filter applied.
        var defFilter = request.DefinitionId.HasValue
            ? "AND wd.PublicId = @DefinitionId"
            : string.Empty;

        // DATEDIFF(HOUR,...) truncates — use SECOND / 3600.0 for fractional hours in HoursInState
        var sql = $"""
            SELECT
                wi.PublicId                                                 AS InstanceId,
                wd.PublicId                                                 AS DefinitionId,
                COALESCE(ws.DisplayNameVi, ws.Name, '')                     AS StateName,
                CAST(
                    DATEDIFF(SECOND, wi.CurrentStateEnteredAt, SYSDATETIMEOFFSET())
                    / 3600.0
                    AS FLOAT)                                               AS HoursInState,
                wi.CurrentStateEnteredAt                                    AS EnteredAt
            FROM workflow.WorkflowInstances wi
            INNER JOIN workflow.WorkflowDefinitions wd ON wd.Id = wi.DefinitionId
            LEFT JOIN workflow.WorkflowStates ws ON ws.Id = wi.CurrentStateId
            WHERE wi.TenantId = @TenantId
              AND wi.CompletedAt IS NULL
              AND DATEDIFF(HOUR, wi.CurrentStateEnteredAt, SYSDATETIMEOFFSET()) > @MinHoursInState
              {defFilter}
            ORDER BY HoursInState DESC
            """;

        var rows = await db.QueryAsync<AgingInstanceDto>(
            sql,
            new { request.TenantId, request.MinHoursInState, request.DefinitionId },
            cancellationToken);

        var result = rows.ToList();
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15), cancellationToken);
        return Result.Ok<IReadOnlyList<AgingInstanceDto>>(result);
    }
}
