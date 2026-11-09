using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Infrastructure.Caching;

/// <summary>
/// Produces tenant-scoped cache keys using the pattern: t:{tenantId}:{key}
///
/// All cache keys for tenant-scoped data MUST be prefixed with the tenant ID
/// to prevent cross-tenant cache leaks in L1 (IMemoryCache) and L2 (Redis).
///
/// Usage:
///   var key = TenantCacheKeyFactory.Build(tenantContext, "cases:list:active");
///   // → "t:3fa85f64-...:cases:list:active"
///
///   // For system-wide (non-tenant) data use a plain prefix:
///   var key = TenantCacheKeyFactory.BuildSystem("config:feature-flags");
///   // → "sys:config:feature-flags"
/// </summary>
public static class TenantCacheKeyFactory
{
    private const string TenantPrefix = "t";
    private const string SystemPrefix = "sys";

    /// <summary>
    /// Builds a tenant-scoped key. Throws if tenantId is null (caller must guard).
    /// </summary>
    public static string Build(ITenantContext tenantContext, string key)
    {
        if (tenantContext.TenantId is null)
            throw new InvalidOperationException(
                $"Cannot build tenant-scoped cache key for '{key}' — TenantId is null. " +
                "Use BuildSystem() for non-tenant data or ensure the request has a tenant context.");

        return $"{TenantPrefix}:{tenantContext.TenantId.Value}:{key}";
    }

    /// <summary>
    /// Builds a tenant-scoped prefix for use with ICacheService.RemoveByPrefixAsync.
    /// Invalidates all cache entries for the given tenant.
    /// </summary>
    public static string BuildPrefix(ITenantContext tenantContext)
    {
        if (tenantContext.TenantId is null)
            throw new InvalidOperationException("Cannot build tenant cache prefix — TenantId is null.");

        return $"{TenantPrefix}:{tenantContext.TenantId.Value}:";
    }

    /// <summary>
    /// Builds a tenant-scoped prefix scoped to a specific domain area.
    /// E.g. BuildPrefix(ctx, "cases") → "t:{tenantId}:cases:"
    /// </summary>
    public static string BuildPrefix(ITenantContext tenantContext, string domain)
    {
        if (tenantContext.TenantId is null)
            throw new InvalidOperationException("Cannot build tenant cache prefix — TenantId is null.");

        return $"{TenantPrefix}:{tenantContext.TenantId.Value}:{domain}:";
    }

    /// <summary>
    /// Builds a system-wide (non-tenant-scoped) cache key.
    /// Only for data that is identical across all tenants (e.g. feature flags, system config).
    /// </summary>
    public static string BuildSystem(string key) => $"{SystemPrefix}:{key}";
}
