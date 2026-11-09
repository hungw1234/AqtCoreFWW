using System.Text.Json;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SystemParams.Application.Services;
using AqtCoreFW.SystemParams.Domain.Entities;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.SystemParams.Infrastructure.Services;

/// <summary>
/// Singleton — IServiceScopeFactory for scoped EF access.
/// Cache key: "sysparam:{tenantId|system}:{key}" (ICacheService handles L1+L2).
/// On SetAsync for feature flags: publishes Redis ff-invalidate channel for cross-pod propagation.
/// </summary>
public class SystemParamService(
    IServiceScopeFactory scopeFactory,
    ICacheService cache,
    IConnectionMultiplexer redis,
    ILogger<SystemParamService> logger) : ISystemParamService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const string FfInvalidateChannel = "ff-invalidate";

    // --- Non-generic string access (cache-aware) ---

    public async Task<string?> GetAsync(string key, string? tenantId = null, CancellationToken ct = default)
    {
        var normalizedKey = key.ToLowerInvariant();

        if (tenantId is not null)
        {
            var tenantCacheKey = CacheKey(normalizedKey, tenantId);
            var tenantCached = await cache.GetAsync<string>(tenantCacheKey, ct);
            if (tenantCached is not null) return tenantCached;

            var tenantParam = await QueryParamAsync(normalizedKey, tenantId, ct);
            if (tenantParam is not null)
            {
                await cache.SetAsync(tenantCacheKey, tenantParam.Value, CacheTtl, ct);
                return tenantParam.Value;
            }
            // fall through to system-wide
        }

        var sysCacheKey = CacheKey(normalizedKey, null);
        var sysCached = await cache.GetAsync<string>(sysCacheKey, ct);
        if (sysCached is not null) return sysCached;

        var sysParam = await QueryParamAsync(normalizedKey, null, ct);
        if (sysParam is null) return null;

        await cache.SetAsync(sysCacheKey, sysParam.Value, CacheTtl, ct);
        return sysParam.Value;
    }

    public Task SetAsync(string key, object value, CancellationToken ct = default)
        => SetInternalAsync(key, value, null, ct);

    public Task SetAsync(string key, object value, string tenantId, CancellationToken ct = default)
        => SetInternalAsync(key, value, tenantId, ct);

    // --- Typed access — throws KeyNotFoundException on miss ---

    public Task<T> GetAsync<T>(string key, CancellationToken ct = default)
        => GetInternalAsync<T>(key, null, ct);

    public Task<T> GetAsync<T>(string key, string tenantId, CancellationToken ct = default)
        => GetInternalAsync<T>(key, tenantId, ct);

    public async Task<T> GetOrDefaultAsync<T>(string key, T defaultValue, CancellationToken ct = default)
    {
        try { return await GetInternalAsync<T>(key, null, ct); }
        catch (KeyNotFoundException) { return defaultValue; }
    }

    // --- Internals ---

    private async Task<T> GetInternalAsync<T>(string key, string? tenantId, CancellationToken ct)
    {
        var normalizedKey = key.ToLowerInvariant();

        if (tenantId is not null)
        {
            var tenantCacheKey = CacheKey(normalizedKey, tenantId);
            var tenantCached = await cache.GetAsync<string>(tenantCacheKey, ct);
            if (tenantCached is not null)
                return Deserialize<T>(tenantCached, SystemParamDataType.String);

            var tenantParam = await QueryParamAsync(normalizedKey, tenantId, ct);
            if (tenantParam is not null)
            {
                await cache.SetAsync(tenantCacheKey, tenantParam.Value, CacheTtl, ct);
                return Deserialize<T>(tenantParam.Value, tenantParam.DataType);
            }
        }

        var sysCacheKey = CacheKey(normalizedKey, null);
        var sysCached = await cache.GetAsync<string>(sysCacheKey, ct);
        if (sysCached is not null)
            return Deserialize<T>(sysCached, SystemParamDataType.String);

        var sysParam = await QueryParamAsync(normalizedKey, null, ct);
        if (sysParam is null)
            throw new KeyNotFoundException($"SystemParameter '{normalizedKey}' not found.");

        await cache.SetAsync(sysCacheKey, sysParam.Value, CacheTtl, ct);
        return Deserialize<T>(sysParam.Value, sysParam.DataType);
    }

    private async Task SetInternalAsync(string key, object value, string? tenantId, CancellationToken ct)
    {
        var normalizedKey = key.ToLowerInvariant();
        var serialized = Serialize(value);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemParamsDbContext>();

        var param = await db.SystemParameters
            .FirstOrDefaultAsync(p => p.Key == normalizedKey && p.TenantId == tenantId, ct);

        if (param is null)
        {
            var dataType = InferDataType(value);
            db.SystemParameters.Add(
                SystemParameter.Create(normalizedKey, serialized, dataType, string.Empty, isEditable: true, tenantId));
        }
        else
        {
            param.UpdateValue(serialized);
        }

        await db.SaveChangesAsync(ct);

        await cache.RemoveAsync(CacheKey(normalizedKey, tenantId), ct);

        // Feature-flag writes publish Redis invalidation so other pods drop L0 entry (<100ms propagation)
        if (normalizedKey.StartsWith("feature:", StringComparison.OrdinalIgnoreCase))
        {
            var flagKey = tenantId is not null ? $"{normalizedKey}:{tenantId}" : normalizedKey;
            try
            {
                await redis.GetSubscriber().PublishAsync(
                    RedisChannel.Literal(FfInvalidateChannel), flagKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis ff-invalidate publish failed for {Key} — L0 will expire on 5min reload", flagKey);
            }
        }

        logger.LogDebug("SystemParam updated: {Key} (tenant={TenantId})", normalizedKey, tenantId ?? "system");
    }

    private async Task<SystemParameter?> QueryParamAsync(string key, string? tenantId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemParamsDbContext>();
        return await db.SystemParameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == key && p.TenantId == tenantId, ct);
    }

    private static string CacheKey(string key, string? tenantId) =>
        $"sysparam:{tenantId ?? "system"}:{key}";

    private static T Deserialize<T>(string value, SystemParamDataType dataType) =>
        dataType switch
        {
            SystemParamDataType.Int  => (T)(object)int.Parse(value),
            SystemParamDataType.Bool => (T)(object)bool.Parse(value),
            SystemParamDataType.Json => JsonSerializer.Deserialize<T>(value)
                                        ?? throw new InvalidCastException($"Cannot deserialize '{value}' to {typeof(T).Name}"),
            _                        => typeof(T) == typeof(string)
                                        ? (T)(object)value
                                        : JsonSerializer.Deserialize<T>(value)!,
        };

    private static string Serialize(object value) =>
        value switch
        {
            string s => s,
            bool b   => b.ToString().ToLowerInvariant(),
            int i    => i.ToString(),
            _        => JsonSerializer.Serialize(value),
        };

    private static SystemParamDataType InferDataType(object value) =>
        value switch
        {
            string => SystemParamDataType.String,
            int    => SystemParamDataType.Int,
            bool   => SystemParamDataType.Bool,
            _      => SystemParamDataType.Json,
        };
}
