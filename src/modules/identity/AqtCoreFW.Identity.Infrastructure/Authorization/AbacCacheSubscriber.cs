using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// Cross-pod ABAC L1 cache invalidation subscriber.
/// Subscribes Redis channel <c>abac-invalidate</c> → evicts local <see cref="IMemoryCache"/>
/// entry trên message publish từ pod khác.
///
/// Startup retry: 5 attempts với exponential backoff 5s→10s→20s→40s→60s.
/// Final failure fallback: natural L1 TTL (10 min default) — cross-pod invalidation disabled
/// cho đến khi subscriber khởi động lại (manual redeploy/restart).
///
/// Match <c>FeatureFlagSubscriber</c> precedent + thêm retry loop (R2).
/// </summary>
public sealed class AbacCacheSubscriber : IHostedService
{
    private const string ChannelName = "abac-invalidate";
    private static readonly int[] RetryDelaysSeconds = [5, 10, 20, 40, 60];

    private readonly IConnectionMultiplexer _redis;
    private readonly IMemoryCache _l1Cache;
    private readonly ILogger<AbacCacheSubscriber> _logger;
    private ISubscriber? _subscriber;

    public AbacCacheSubscriber(
        IConnectionMultiplexer redis,
        IMemoryCache l1Cache,
        ILogger<AbacCacheSubscriber> logger)
    {
        _redis = redis;
        _l1Cache = l1Cache;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < RetryDelaysSeconds.Length; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            try
            {
                _subscriber = _redis.GetSubscriber();
                await _subscriber.SubscribeAsync(
                    RedisChannel.Literal(ChannelName),
                    (_, message) =>
                    {
                        if (!message.IsNullOrEmpty)
                            _l1Cache.Remove(message.ToString());
                    });

                _logger.LogInformation(
                    "AbacCacheSubscriber subscribed to '{Channel}' after {Attempt} attempt(s)",
                    ChannelName, attempt + 1);
                return; // success — exit retry loop
            }
            catch (OperationCanceledException)
            {
                // Shutdown signal — propagate lên host để graceful stop, KHÔNG retry.
                throw;
            }
            catch (Exception ex)
            {
                var delay = RetryDelaysSeconds[attempt];
                _logger.LogWarning(ex,
                    "AbacCacheSubscriber subscribe attempt {Attempt}/{Max} failed; retry in {Delay}s",
                    attempt + 1, RetryDelaysSeconds.Length, delay);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Token cancel trong lúc chờ — graceful exit, KHÔNG throw.
                    return;
                }
            }
        }

        _logger.LogError(
            "AbacCacheSubscriber FAILED sau {Max} retry attempts — cross-pod L1 invalidation DISABLED. " +
            "Fallback: natural L1 expiry (~10min). Cần Redis restore + pod restart để khôi phục cross-pod sync.",
            RetryDelaysSeconds.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
        {
            try
            {
                _subscriber.Unsubscribe(RedisChannel.Literal(ChannelName));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AbacCacheSubscriber unsubscribe failed during shutdown");
            }
        }
        return Task.CompletedTask;
    }
}
