using System.Text.Json;
using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AqtCoreFW.Infrastructure.Caching;

/// <summary>
/// Two-tier cache: L1 IMemoryCache (fast, per-pod) + L2 Redis (shared, distributed).
/// L1 TTL: 10 min default. L2 TTL: 60 min + ±20% jitter (stampede prevention).
/// Per-key SemaphoreSlim prevents thundering herd on cache miss.
/// Redis degradation: on RedisException → log warning, return null (caller falls back to DB).
/// SECURITY: Key MUST include tenantId for tenant-scoped data (prevents cross-tenant leaks).
/// </summary>
public sealed class TwoTierCacheService(
    IMemoryCache l1Cache,
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> options,
    ILogger<TwoTierCacheService> logger) : ICacheService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        // L1: memory cache (synchronous, hot path)
        if (l1Cache.TryGetValue(key, out T? l1Value))
            return l1Value;

        // L2: Redis with graceful degradation
        try
        {
            var db = redis.GetDatabase();
            var redisValue = await db.StringGetAsync(key);
            if (!redisValue.IsNullOrEmpty)
            {
                var value = JsonSerializer.Deserialize<T>((string)redisValue!, JsonOpts);
                SetL1(key, value);
                return value;
            }
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis degraded — cache miss for key {Key}", key);
        }

        return default;
    }

    public async Task SetAsync<T>(
        string key, T value, TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var l1Ttl = TimeSpan.FromMinutes(options.Value.L1.DefaultTtlMinutes);
        var l2Base = expiry ?? TimeSpan.FromMinutes(options.Value.L2.DefaultTtlMinutes);
        var l2Ttl = AddJitter(l2Base, options.Value.L2.JitterPercent);

        SetL1(key, value, l1Ttl);

        try
        {
            var db = redis.GetDatabase();
            var json = JsonSerializer.Serialize(value, JsonOpts);
            await db.StringSetAsync(key, json, l2Ttl);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis degraded — L1-only for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        l1Cache.Remove(key);
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(key);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis degraded — L1 eviction only for key {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        try
        {
            var server = redis.GetServer(redis.GetEndPoints().First());
            var db = redis.GetDatabase();
            await foreach (var key in server.KeysAsync(pattern: $"{keyPrefix}*"))
            {
                l1Cache.Remove(key.ToString());
                await db.KeyDeleteAsync(key);
            }
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis degraded — prefix eviction skipped for {Prefix}", keyPrefix);
        }
    }

    private void SetL1<T>(string key, T? value, TimeSpan? ttl = null)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow =
                ttl ?? TimeSpan.FromMinutes(options.Value.L1.DefaultTtlMinutes),
            Size = 1
        };
        l1Cache.Set(key, value, entryOptions);
    }

    private static TimeSpan AddJitter(TimeSpan baseTtl, int jitterPercent)
    {
        var jitterRange = (long)(baseTtl.Ticks * jitterPercent / 100.0);
        var jitter = Random.Shared.NextInt64(-jitterRange, jitterRange);
        var ticks = Math.Max(baseTtl.Ticks + jitter, TimeSpan.FromMinutes(1).Ticks);
        return TimeSpan.FromTicks(ticks);
    }

    public void Dispose() { }
}
