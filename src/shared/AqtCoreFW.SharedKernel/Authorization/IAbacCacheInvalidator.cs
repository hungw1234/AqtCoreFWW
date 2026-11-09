namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// Invalidates cached ABAC attribute rules after Create/Update/Delete write operations (F-17).
///
/// Two-Tier + cross-pod semantics:
///   1. Evict L1 (IMemoryCache) + L2 (Redis) on THIS pod via ICacheService.RemoveAsync.
///   2. Publish to Redis channel "abac-invalidate" so AbacCacheSubscriber on OTHER pods
///      evicts their L1 (L2 already gone — single shared Redis).
///
/// Key format must match AbacAuthorizationHandler — use <see cref="AbacCacheKeys.ForDepartment"/>.
/// </summary>
public interface IAbacCacheInvalidator
{
    /// <summary>
    /// Invalidate cache cho 1 (tenant, attributeValue) pair cụ thể — evict this-pod L1+L2
    /// và publish cross-pod L1 eviction message.
    /// </summary>
    /// <param name="tenantId">Null cho system-wide rules; Guid cho tenant-scoped.</param>
    /// <param name="attributeValue">Ví dụ department code ("CNTT", "TCCB").</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvalidateAsync(Guid? tenantId, string attributeValue, CancellationToken ct = default);
}
