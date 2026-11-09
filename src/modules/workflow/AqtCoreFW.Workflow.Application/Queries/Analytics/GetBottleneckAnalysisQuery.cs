using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.Analytics;

/// <summary>
/// States with highest average dwell time + current active instance count.
/// Dwell computed via LEAD window function on history rows.
/// Tenant scoped via JOIN to WorkflowInstances (history has no TenantId).
/// </summary>
public sealed record GetBottleneckAnalysisQuery(
    Guid DefinitionId,
    Guid TenantId,
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<Result<IReadOnlyList<BottleneckDto>>>;

public sealed class GetBottleneckAnalysisQueryHandler(
    IReadDbConnection db,
    ICacheService cache)
    : IRequestHandler<GetBottleneckAnalysisQuery, Result<IReadOnlyList<BottleneckDto>>>
{
    public async Task<Result<IReadOnlyList<BottleneckDto>>> Handle(
        GetBottleneckAnalysisQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"wf:analytics:{request.TenantId}:{request.DefinitionId}:bottleneck:{request.From:yyyyMMdd}-{request.To:yyyyMMdd}";
        var cached = await cache.GetAsync<List<BottleneckDto>>(cacheKey, cancellationToken);
        if (cached != null)
            return Result.Ok<IReadOnlyList<BottleneckDto>>(cached);

        // DwellCte: LEAD(TransitionedAt) - TransitionedAt per row = time spent before next transition.
        // ActiveCte: count of currently active (non-completed) instances per state for this definition.
        // Final JOIN on StateId to get state name + active count.
        // Tenant scoped: h JOIN wi WHERE wi.TenantId = @TenantId.
        // T2 WorkflowDefinition: DefinitionId request param is Guid (PublicId) — join through wd to resolve
        // to bigint PK before filtering wi.DefinitionId (bigint FK).
        const string sql = """
            WITH DwellCte AS (
                SELECT
                    h.FromStateId,
                    CAST(
                        DATEDIFF(SECOND,
                            h.TransitionedAt,
                            LEAD(h.TransitionedAt) OVER (
                                PARTITION BY h.InstanceId
                                ORDER BY h.TransitionedAt
                            )
                        ) / 60.0
                        AS FLOAT
                    ) AS DwellMinutes
                FROM workflow.WorkflowInstanceHistories h
                INNER JOIN workflow.WorkflowInstances wi ON wi.Id = h.InstanceId
                INNER JOIN workflow.WorkflowDefinitions wd ON wd.Id = wi.DefinitionId
                WHERE wd.PublicId = @DefinitionId
                  AND wi.TenantId = @TenantId
                  AND h.TransitionedAt BETWEEN @From AND @To
            ),
            AvgDwellCte AS (
                SELECT
                    FromStateId,
                    AVG(DwellMinutes) AS AvgDwellMinutes
                FROM DwellCte
                WHERE DwellMinutes IS NOT NULL
                GROUP BY FromStateId
            ),
            ActiveCte AS (
                SELECT
                    wi.CurrentStateId  AS StateId,
                    COUNT(*)           AS ActiveCount
                FROM workflow.WorkflowInstances wi
                INNER JOIN workflow.WorkflowDefinitions wd ON wd.Id = wi.DefinitionId
                WHERE wd.PublicId = @DefinitionId
                  AND wi.TenantId = @TenantId
                  AND wi.CompletedAt IS NULL
                GROUP BY wi.CurrentStateId
            )
            SELECT
                a.FromStateId                               AS StateId,
                COALESCE(ws.DisplayNameVi, ws.Name, '')    AS StateName,
                a.AvgDwellMinutes,
                COALESCE(ac.ActiveCount, 0)                AS ActiveCount
            FROM AvgDwellCte a
            LEFT JOIN ActiveCte ac ON ac.StateId = a.FromStateId
            LEFT JOIN workflow.WorkflowStates ws ON ws.Id = a.FromStateId
            ORDER BY a.AvgDwellMinutes DESC
            """;

        var rows = await db.QueryAsync<BottleneckDto>(
            sql,
            new { request.DefinitionId, request.TenantId, request.From, request.To },
            cancellationToken);

        var result = rows.ToList();
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15), cancellationToken);
        return Result.Ok<IReadOnlyList<BottleneckDto>>(result);
    }
}
