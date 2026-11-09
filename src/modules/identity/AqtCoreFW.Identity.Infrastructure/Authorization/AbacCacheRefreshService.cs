using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// T4-05-FU — Periodic safety net for ABAC L1+L2 cache staleness.
///
/// Bookends <see cref="AbacCacheSubscriber"/>: R2 retry covers 95% of subscriber-fail
/// scenarios; this closes the residual 5% by sweeping all ABAC entries every
/// <see cref="RefreshInterval"/> regardless of pub/sub health. Mirrors
/// <c>FeatureFlagRefreshService</c> precedent.
///
/// Prefixes match <see cref="AqtCoreFW.SharedKernel.Authorization.AbacCacheKeys"/>:
///   - <c>sys:abac:</c> — global (null tenant) rules
///   - <c>t:*:abac:</c> — tenant-scoped rules (Redis KEYS glob matches mid-pattern <c>*</c>)
/// </summary>
public sealed class AbacCacheRefreshService(
    ICacheService cache,
    ILogger<AbacCacheRefreshService> logger) : BackgroundService
{
    internal const string SystemPrefix = "sys:abac:";
    internal const string TenantPrefix = "t:*:abac:";
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No initial sweep — handler repopulates from DB on first request after start.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "AbacCacheRefreshService: periodic ABAC cache sweep failed; will retry next tick");
            }
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await cache.RemoveByPrefixAsync(SystemPrefix, cancellationToken);
        await cache.RemoveByPrefixAsync(TenantPrefix, cancellationToken);
    }
}
