namespace AqtCoreFW.SharedKernel.Application.Caching;

/// <summary>
/// Two-tier cache abstraction: L1 IMemoryCache (10min) + L2 Redis (60min + jitter).
/// IMPORTANT: Key MUST include tenantId to prevent cross-tenant data leaks.
/// Key pattern: "{prefix}:{tenantId}:{key}"
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);
}
