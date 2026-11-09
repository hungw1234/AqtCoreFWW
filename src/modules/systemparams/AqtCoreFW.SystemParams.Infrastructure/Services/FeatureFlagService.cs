using AqtCoreFW.SystemParams.Application.Services;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AqtCoreFW.SystemParams.Infrastructure.Services;

/// <summary>
/// Singleton — ConcurrentDict L0 cache for O(1) sync feature flag reads.
/// Per-tenant key stored as "{flagKey}:{tenantId}" with fallback to "{flagKey}".
/// Reloaded every 5min by FeatureFlagRefreshService.
/// Invalidated per-key by FeatureFlagSubscriber on Redis ff-invalidate channel.
/// </summary>
public class FeatureFlagService(
    IServiceScopeFactory scopeFactory,
    ILogger<FeatureFlagService> logger) : IFeatureFlagService
{
    private readonly ConcurrentDictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string key, string? tenantId = null)
    {
        if (tenantId is not null)
        {
            var tenantKey = $"{key}:{tenantId}";
            if (_flags.TryGetValue(tenantKey, out var tenantVal))
                return tenantVal;
        }
        return _flags.TryGetValue(key, out var globalVal) && globalVal;
    }

    public void Invalidate(string key)
    {
        _flags.TryRemove(key, out _);
        logger.LogDebug("FeatureFlag L0 invalidated: {Key}", key);
    }

    public async Task ReloadAllAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemParamsDbContext>();

        var flags = await db.SystemParameters.AsNoTracking()
            .Where(p => p.Key.StartsWith("feature:"))
            .ToListAsync(ct);

        foreach (var flag in flags)
        {
            var enabled = bool.TryParse(flag.Value, out var v) && v;
            var dictKey = flag.TenantId is not null ? $"{flag.Key}:{flag.TenantId}" : flag.Key;
            _flags[dictKey] = enabled;
        }

        logger.LogDebug("FeatureFlag L0 reloaded: {Count} flags", flags.Count);
    }
}
