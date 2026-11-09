namespace AqtCoreFW.BusinessProcess.Application.Caching;

/// <summary>
/// Cache-key conventions for the BusinessProcess module. Single source of truth — readers and writers
/// in Application layer compose keys here so a typo can't leak across tenants or skip invalidation.
/// Pattern: <c>bp:{tenantId}:&lt;suffix&gt;</c> — tenantId always included per ICacheService rule.
/// </summary>
public static class BusinessProcessCacheKeys
{
    /// <summary>Prefix scoped to one tenant. Used for <c>RemoveByPrefixAsync</c> on any mutation.</summary>
    public static string TenantPrefix(Guid tenantId) => $"bp:{tenantId}:";

    /// <summary>Active-list picker cache key — populated by the <c>/active</c> read path only.</summary>
    public static string ActiveList(Guid tenantId) => $"bp:{tenantId}:active";
}
