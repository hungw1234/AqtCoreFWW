namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Invalidates Redis caches related to user role/permission resolution.
/// Two key namespaces: <c>user-roles:{userId}</c> (claims enrichment, ClaimsEnrichmentTransformer)
/// and <c>perm:{userId}</c> (effective permissions, EffectivePermissionService).
/// Call after role definition changes (permissions/activation) to flush stale caches.
/// </summary>
public interface IBulkRoleChangeService
{
    /// <summary>Scans all user-roles:* keys and deletes them.</summary>
    Task InvalidateRoleCacheForAllUsersAsync();

    /// <summary>Invalidate role cache for a specific set of users.</summary>
    Task InvalidateRoleCacheForUsersAsync(IReadOnlyList<Guid> userIds);

    /// <summary>
    /// Invalidate effective-permission cache (<c>perm:{userId}</c>) for the given users.
    /// Required after role-permission mapping changes — otherwise stale codes survive up to
    /// 10-minute Redis TTL + 10-minute React Query staleTime = 20-minute UI lag (RBAC-1 v2.133p7).
    /// </summary>
    Task InvalidatePermissionCacheForUsersAsync(IReadOnlyList<Guid> userIds);
}
