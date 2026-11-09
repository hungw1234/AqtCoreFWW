using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.Analytics;

/// <summary>
/// SLA compliance trend: completed instances where CompletedAt &lt;= DueAt are "on-time".
/// Grouped by time period. Only completed instances (CompletedAt IS NOT NULL, DueAt IS NOT NULL).
/// Tenant scoped via wi.TenantId.
/// </summary>
public sealed record GetSlaComplianceTrendQuery(
    Guid TenantId,
    DateTimeOffset From,
    DateTimeOffset To,
    GroupByPeriod GroupBy,
    Guid? DefinitionId = null) : IRequest<Result<IReadOnlyList<SlaComplianceTrendDto>>>;

public sealed class GetSlaComplianceTrendQueryHandler(
    IReadDbConnection db,
    ICacheService cache)
    : IRequestHandler<GetSlaComplianceTrendQuery, Result<IReadOnlyList<SlaComplianceTrendDto>>>
{
    public async Task<Result<IReadOnlyList<SlaComplianceTrendDto>>> Handle(
        GetSlaComplianceTrendQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"wf:analytics:{request.TenantId}:{request.DefinitionId ?? Guid.Empty}:sla-trend-{request.GroupBy}:{request.From:yyyyMMdd}-{request.To:yyyyMMdd}";
        var cached = await cache.GetAsync<List<SlaComplianceTrendDto>>(cacheKey, cancellationToken);
        if (cached != null)
            return Result.Ok<IReadOnlyList<SlaComplianceTrendDto>>(cached);

        // Hardcoded period expressions — GroupByPeriod enum prevents SQL injection
        var periodExpr = request.GroupBy switch
        {
            GroupByPeriod.Day   => "CAST(wi.CompletedAt AS DATE)",
            GroupByPeriod.Week  => "CONCAT(YEAR(wi.CompletedAt), '-W', FORMAT(DATEPART(ISO_WEEK, wi.CompletedAt), '00'))",
            GroupByPeriod.Month => "FORMAT(wi.CompletedAt, 'yyyy-MM')",
            _                   => throw new ArgumentOutOfRangeException(nameof(request.GroupBy))
        };

        // T2 WorkflowDefinition: optional DefinitionId filter is Guid (PublicId), wi.DefinitionId is bigint FK.
        // When filter active: INNER JOIN wd to resolve PublicId → bigint PK before filtering.
        // When filter absent: no JOIN needed (avoids unnecessary cross-join cost).
        var (defJoin, defFilter) = request.DefinitionId.HasValue
            ? ("INNER JOIN workflow.WorkflowDefinitions wd ON wd.Id = wi.DefinitionId",
               "AND wd.PublicId = @DefinitionId")
            : (string.Empty, string.Empty);

        var sql = $"""
            SELECT
                {periodExpr}                                                AS Period,
                -- COUNT(*) >= 1 per group (GROUP BY implies ≥1 row); no divide-by-zero possible
                COUNT(*)                                                    AS Total,
                SUM(CASE WHEN wi.CompletedAt <= wi.DueAt THEN 1 ELSE 0 END) AS OnTime,
                CAST(
                    100.0 * SUM(CASE WHEN wi.CompletedAt <= wi.DueAt THEN 1 ELSE 0 END)
                    / COUNT(*)
                    AS DECIMAL(5,2))                                        AS CompliancePercent
            FROM workflow.WorkflowInstances wi
            {defJoin}
            WHERE wi.TenantId = @TenantId
              AND wi.CompletedAt IS NOT NULL
              AND wi.DueAt IS NOT NULL
              AND wi.CompletedAt BETWEEN @From AND @To
              {defFilter}
            GROUP BY {periodExpr}
            ORDER BY Period
            """;

        var rows = await db.QueryAsync<SlaComplianceTrendDto>(
            sql,
            new { request.TenantId, request.From, request.To, request.DefinitionId },
            cancellationToken);

        var result = rows.ToList();
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15), cancellationToken);
        return Result.Ok<IReadOnlyList<SlaComplianceTrendDto>>(result);
    }
}
