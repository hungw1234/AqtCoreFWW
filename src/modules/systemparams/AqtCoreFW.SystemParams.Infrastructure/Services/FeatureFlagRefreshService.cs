using AqtCoreFW.SystemParams.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.SystemParams.Infrastructure.Services;

/// <summary>Background service: reloads all feature flags every 5 minutes.</summary>
public sealed class FeatureFlagRefreshService(
    IFeatureFlagService featureFlagService,
    ILogger<FeatureFlagRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial load
        await featureFlagService.ReloadAllAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(RefreshInterval, stoppingToken);
            try
            {
                await featureFlagService.ReloadAllAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "FeatureFlagRefreshService: reload failed");
            }
        }
    }
}
