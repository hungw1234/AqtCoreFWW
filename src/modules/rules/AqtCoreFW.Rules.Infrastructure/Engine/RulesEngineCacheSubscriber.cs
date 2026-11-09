using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.Rules.Infrastructure.Engine;

/// <summary>
/// Subscribes to Redis "rules:cache-invalidate" channel — removes compiled engine from IMemoryCache
/// when another pod activates a new RuleSet version. Falls back gracefully if Redis unavailable.
/// </summary>
public sealed class RulesEngineCacheSubscriber(
    IConnectionMultiplexer redis,
    IMemoryCache cache,
    ILogger<RulesEngineCacheSubscriber> logger) : IHostedService
{
    private const string Channel = "rules:cache-invalidate";
    private ISubscriber? _subscriber;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _subscriber = redis.GetSubscriber();
            await _subscriber.SubscribeAsync(
                RedisChannel.Literal(Channel),
                (_, message) =>
                {
                    if (message.IsNullOrEmpty)
                        return;

                    var payload = (string)message!;
                    if (!Guid.TryParse(payload, out var ruleSetId))
                    {
                        logger.LogWarning("RulesEngineCacheSubscriber: malformed payload '{Payload}' — skipping", payload);
                        return;
                    }

                    cache.Remove(RulesCacheKeys.ForEngine(ruleSetId));
                    logger.LogDebug("RulesEngine cache invalidated via Redis: {RuleSetId}", ruleSetId);
                });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RulesEngineCacheSubscriber: Redis subscription failed — degraded mode (local invalidation only)");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
            await _subscriber.UnsubscribeAllAsync();
    }
}
