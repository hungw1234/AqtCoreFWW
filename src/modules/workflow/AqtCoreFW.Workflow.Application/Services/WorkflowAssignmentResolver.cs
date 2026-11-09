using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Cache-aside resolver: L1/L2 cache (24h TTL) → Dapper fallback.
/// Key: "wf:assignment:{tenantId}:{entityType}:{entitySubType|all}"
/// SQL picks the single best match: highest Specificity, then highest Priority,
/// within active + effective date window.
/// </summary>
public sealed class WorkflowAssignmentResolver(
    ICacheService cache,
    IReadDbConnection db) : IWorkflowAssignmentResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private const string ResolveSql = """
        SELECT TOP 1 WorkflowDefinitionId
        FROM workflow.WorkflowAssignmentRules
        WHERE TenantId = @TenantId
          AND IsActive = 1
          AND IsDeleted = 0
          AND EffectiveFrom <= @Now
          AND (EffectiveUntil IS NULL OR EffectiveUntil > @Now)
          AND (EntityType = @EntityType OR EntityType IS NULL)
          AND (EntitySubType = @EntitySubType OR EntitySubType IS NULL)
        ORDER BY Specificity DESC, Priority DESC
        """;

    public async Task<Result<Guid>> ResolveAsync(
        Guid tenantId,
        string entityType,
        string? entitySubType = null,
        CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(tenantId, entityType, entitySubType);

        // L1/L2 cache hit
        var cached = await cache.GetAsync<Guid?>(cacheKey, ct);
        if (cached.HasValue && cached.Value != Guid.Empty)
            return Result.Ok(cached.Value);

        // Cache miss — query DB
        var rows = await db.QueryAsync<Guid>(
            ResolveSql,
            new
            {
                TenantId = tenantId,
                EntityType = entityType,
                EntitySubType = entitySubType,
                Now = DateTimeOffset.UtcNow
            },
            ct);

        var definitionId = rows.FirstOrDefault();
        if (definitionId == Guid.Empty)
            return Result.Fail($"No workflow assignment rule found for tenant '{tenantId}', entityType '{entityType}', subType '{entitySubType}'.");

        await cache.SetAsync(cacheKey, (Guid?)definitionId, CacheTtl, ct);
        return Result.Ok(definitionId);
    }

    /// <summary>Build tenant-scoped cache key. Key pattern: wf:assignment:{tenantId}:{entityType}:{subType|all}</summary>
    internal static string BuildCacheKey(Guid tenantId, string entityType, string? entitySubType) =>
        $"wf:assignment:{tenantId}:{entityType}:{entitySubType ?? "all"}";
}
