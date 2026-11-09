using AqtCoreFW.SharedKernel.Application.Caching;

namespace AqtCoreFW.Infrastructure.Caching;

/// <summary>
/// No-op cache implementation — always returns null/miss.
/// Use in tests or when caching is explicitly disabled.
/// </summary>
public sealed class NullCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
