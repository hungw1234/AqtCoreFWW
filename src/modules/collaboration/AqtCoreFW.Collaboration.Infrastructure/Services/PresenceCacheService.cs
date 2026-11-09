using AqtCoreFW.Collaboration.Application.Services;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.Collaboration.Infrastructure.Services;

/// <summary>
/// IMemoryCache-backed presence service.
/// Keys expire after 5 minutes of inactivity — clients refresh on heartbeat.
/// </summary>
public sealed class PresenceCacheService(IMemoryCache cache) : IPresenceCacheService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public Task SetPresenceAsync(Guid userId, string status)
    {
        cache.Set(CacheKey(userId), status, Ttl);
        return Task.CompletedTask;
    }

    public Task<string?> GetPresenceAsync(Guid userId)
    {
        cache.TryGetValue(CacheKey(userId), out string? status);
        return Task.FromResult(status);
    }

    private static string CacheKey(Guid userId) => $"presence:{userId}";
}
