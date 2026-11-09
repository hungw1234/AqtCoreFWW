using AqtCoreFW.SystemParams.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.SystemParams.Infrastructure.Services;

/// <summary>
/// Subscribes to Redis "ff-invalidate" channel — invalidates L0 cache on targeted flag changes.
/// Falls back gracefully if Redis is unavailable (FeatureFlagRefreshService handles 5min reload).
/// </summary>
public sealed class FeatureFlagSubscriber(
    IConnectionMultiplexer redis,
    IFeatureFlagService featureFlagService,
    ILogger<FeatureFlagSubscriber> logger) : IHostedService
{
    private ISubscriber? _subscriber;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _subscriber = redis.GetSubscriber();
            await _subscriber.SubscribeAsync(
                RedisChannel.Literal("ff-invalidate"),
                (_, message) =>
                {
                    if (!message.IsNullOrEmpty)
                    {
                        featureFlagService.Invalidate(message!);
                        logger.LogDebug("FeatureFlag invalidated via Redis: {Key}", (string)message!);
                    }
                });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FeatureFlagSubscriber: Redis subscription failed — degraded mode (5min refresh only)");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
            await _subscriber.UnsubscribeAllAsync();
    }
}
