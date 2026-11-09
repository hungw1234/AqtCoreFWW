using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.Analytics;

/// <summary>
/// Average and median minutes spent in each state, computed from consecutive transition timestamps.
/// Tenant scoped via JOIN to WorkflowInstances (history table has no TenantId column).
/// </summary>
public sealed record GetAvgProcessingTimeByStateQuery(
    Guid DefinitionId,
    Guid TenantId,
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<Result<IReadOnlyList<StateProcessingTimeDto>>>;

public sealed class GetAvgProcessingTimeByStateQueryHandler(
    IReadDbConnection db,
    ICacheService cache)
    : IRequestHandler<GetAvgProcessingTimeByStateQuery, Result<IReadOnlyList<StateProcessingTimeDto>>>
{
    public async Task<Result<IReadOnlyList<StateProcessingTimeDto>>> Handle(
        GetAvgProcessingTimeByStateQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"wf:analytics:{request.TenantId}:{request.DefinitionId}:proc-time:{request.From:yyyyMMdd}-{request.To:yyyyMMdd}";
        var cached = await cache.GetAsync<List<StateProcessingTimeDto>>(cacheKey, cancellationToken);
        if (cached != null)
            return Result.Ok<IReadOnlyList<StateProcessingTimeDto>>(cached);

        // CTE1: compute dwell minutes per history row via LAG window function.
        // CTE2: AVG + COUNT grouped per state.
        // CTE3: median via PERCENTILE_CONT (window function, no GROUP BY) — DISTINCT to deduplicate.
        // Final: JOIN CTE2 + CTE3 on FromStateId.
        // Tenant scoped: h JOIN wi WHERE wi.TenantId = @TenantId (history has no TenantId).
        // T2 WorkflowDefinition: DefinitionId request param is Guid (PublicId) — join wd to resolve bigint PK.
        const string sql = """
            WITH DwellCte AS (
                SELECT
                    h.FromStateId,
                    CAST(
                        DATEDIFF(SECOND,
                            LAG(h.TransitionedAt) OVER (
                                PARTITION BY h.InstanceId
                                ORDER BY h.TransitionedAt
                            ),
                            h.TransitionedAt
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
            AvgCte AS (
                SELECT
                    FromStateId,
                    AVG(DwellMinutes)   AS AvgMinutes,
                    COUNT(*)            AS SampleCount
                FROM DwellCte
                WHERE DwellMinutes IS NOT NULL
                GROUP BY FromStateId
            ),
            MedianCte AS (
                SELECT DISTINCT
                    FromStateId,
                    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY DwellMinutes)
                        OVER (PARTITION BY FromStateId) AS MedianMinutes
                FROM DwellCte
                WHERE DwellMinutes IS NOT NULL
            )
            SELECT
                a.FromStateId                               AS StateId,
                COALESCE(ws.DisplayNameVi, ws.Name, '')    AS StateName,
                a.AvgMinutes,
                m.MedianMinutes,
                a.SampleCount
            FROM AvgCte a
            INNER JOIN MedianCte m ON m.FromStateId = a.FromStateId
            LEFT JOIN workflow.WorkflowStates ws ON ws.Id = a.FromStateId
            ORDER BY a.AvgMinutes DESC
            """;

        var rows = await db.QueryAsync<StateProcessingTimeDto>(
            sql,
            new { request.DefinitionId, request.TenantId, request.From, request.To },
            cancellationToken);

        var result = rows.ToList();
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15), cancellationToken);
        return Result.Ok<IReadOnlyList<StateProcessingTimeDto>>(result);
    }
}
