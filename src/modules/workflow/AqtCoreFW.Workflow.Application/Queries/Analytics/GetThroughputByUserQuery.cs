using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Queries.Analytics;

/// <summary>
/// Transition count per user grouped by time period (Day/Week/Month).
/// Returns top-5 users as individual series + 1 aggregated "Others" row per period.
/// GroupBy is resolved to a hardcoded SQL fragment — no user-string interpolation.
/// Tenant scoped via JOIN to WorkflowInstances.
/// </summary>
public sealed record GetThroughputByUserQuery(
    Guid TenantId,
    DateTimeOffset From,
    DateTimeOffset To,
    GroupByPeriod GroupBy,
    Guid? DefinitionId = null) : IRequest<Result<IReadOnlyList<UserThroughputDto>>>;

public sealed class GetThroughputByUserQueryHandler(
    IReadDbConnection db,
    ICacheService cache)
    : IRequestHandler<GetThroughputByUserQuery, Result<IReadOnlyList<UserThroughputDto>>>
{
    // Maximum number of individual user series — the rest are aggregated into "Others"
    private const int TopN = 5;

    public async Task<Result<IReadOnlyList<UserThroughputDto>>> Handle(
        GetThroughputByUserQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"wf:analytics:{request.TenantId}:{request.DefinitionId ?? Guid.Empty}:throughput-{request.GroupBy}:{request.From:yyyyMMdd}-{request.To:yyyyMMdd}";
        var cached = await cache.GetAsync<List<UserThroughputDto>>(cacheKey, cancellationToken);
        if (cached != null)
            return Result.Ok<IReadOnlyList<UserThroughputDto>>(cached);

        // Hardcoded period expressions — GroupByPeriod enum prevents SQL injection
        var periodExpr = request.GroupBy switch
        {
            GroupByPeriod.Day   => "CAST(h.TransitionedAt AS DATE)",
            GroupByPeriod.Week  => "CONCAT(YEAR(h.TransitionedAt), '-W', FORMAT(DATEPART(ISO_WEEK, h.TransitionedAt), '00'))",
            GroupByPeriod.Month => "FORMAT(h.TransitionedAt, 'yyyy-MM')",
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
                h.TransitionedBy    AS UserId,
                {periodExpr}        AS Period,
                COUNT(*)            AS TransitionCount
            FROM workflow.WorkflowInstanceHistories h
            INNER JOIN workflow.WorkflowInstances wi ON wi.Id = h.InstanceId
            {defJoin}
            WHERE wi.TenantId = @TenantId
              AND h.TransitionedAt BETWEEN @From AND @To
              {defFilter}
            GROUP BY h.TransitionedBy, {periodExpr}
            ORDER BY Period, TransitionCount DESC
            """;

        var rows = await db.QueryAsync<UserThroughputDto>(
            sql,
            new { request.TenantId, request.From, request.To, request.DefinitionId },
            cancellationToken);

        var result = ApplyTopNWithOthers(rows);
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15), cancellationToken);
        return Result.Ok<IReadOnlyList<UserThroughputDto>>(result);
    }

    /// <summary>
    /// Partitions raw rows into top-N user series + an "Others" aggregate per period.
    /// Top users are selected by total transition count across all periods.
    /// Others bucket uses Guid.Empty as sentinel and IsOthers=true.
    /// </summary>
    internal static List<UserThroughputDto> ApplyTopNWithOthers(IEnumerable<UserThroughputDto> rows)
    {
        var allRows = rows.ToList();
        if (allRows.Count == 0) return allRows;

        // Determine top-N users by total transitions (stable order)
        var topUserIds = allRows
            .GroupBy(r => r.UserId)
            .OrderByDescending(g => g.Sum(r => r.TransitionCount))
            .Take(TopN)
            .Select(g => g.Key)
            .ToHashSet();

        var result = new List<UserThroughputDto>(allRows.Count);

        // Keep top-N rows as-is
        result.AddRange(allRows.Where(r => topUserIds.Contains(r.UserId)));

        // Aggregate remaining users into one "Others" row per period
        var othersByPeriod = allRows
            .Where(r => !topUserIds.Contains(r.UserId))
            .GroupBy(r => r.Period)
            .Select(g => new UserThroughputDto
            {
                UserId = Guid.Empty, // sentinel value for "Others" bucket
                Period = g.Key,
                TransitionCount = g.Sum(r => r.TransitionCount),
                IsOthers = true,
            });

        result.AddRange(othersByPeriod);
        return result;
    }
}
