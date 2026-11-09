using AqtCoreFW.Identity.Infrastructure.Authorization;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Identity;

/// <summary>
/// T4-05 Phase 01 — Cross-pod ABAC L1 cache invalidation via Redis pub/sub.
///
/// Simulates 2-pod deployment bằng cách instantiate <see cref="AbacCacheSubscriber"/>
/// trực tiếp với real <see cref="IConnectionMultiplexer"/> (từ Testcontainers Redis).
/// <c>WebAppFixture</c> strips <c>IHostedService</c> trong tests, nên subscriber KHÔNG
/// auto-start trong Api factory → test phải manual start/stop subscriber.
///
/// Success criteria: Pod A publish key X → Pod B's local IMemoryCache evicts X trong &lt; 2s.
/// </summary>
[Collection("Integration")]
public sealed class AbacCacheSubscriberIntegrationTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string ChannelName = "abac-invalidate";

    private IConnectionMultiplexer _redis = null!;
    private IMemoryCache _podBCache = null!;
    private AbacCacheSubscriber _subscriber = null!;

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(db.RedisConnectionString);
        _podBCache = new MemoryCache(new MemoryCacheOptions());
        _subscriber = new AbacCacheSubscriber(_redis, _podBCache, NullLogger<AbacCacheSubscriber>.Instance);

        // Subscriber phải StartAsync trước khi publish — normally IHostedService,
        // nhưng WebAppFixture strip → phải manual start.
        await _subscriber.StartAsync(CancellationToken.None);

        // Nhỏ 200ms warm-up cho Redis subscription handshake settle.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
    }

    public async Task DisposeAsync()
    {
        await _subscriber.StopAsync(CancellationToken.None);
        _podBCache.Dispose();
        await _redis.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Subscriber_EvictsLocalL1_WhenChannelMessageReceived()
    {
        // Arrange — seed Pod B's L1 cache với dummy key.
        const string cacheKey = "t:11111111-2222-3333-4444-555555555555:abac:rules:department:CNTT";
        _podBCache.Set(cacheKey, "seeded-rules", TimeSpan.FromMinutes(10));
        _podBCache.TryGetValue(cacheKey, out _).Should().BeTrue("cache seeded precondition");

        // Act — Pod A publishes invalidate signal on channel.
        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(ChannelName), cacheKey);

        // Assert — poll Pod B's cache (pub/sub eventually consistent within sub-second typical).
        var evicted = await PollUntilAsync(
            () => !_podBCache.TryGetValue(cacheKey, out _),
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(50));

        evicted.Should().BeTrue("Pod B's L1 should be evicted within 2s of cross-pod publish");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Subscriber_IgnoresEmptyMessage_DoesNotEvictUnrelatedKeys()
    {
        // Empty payload (safety net — should never happen in prod but guard).
        const string cacheKey = "sys:abac:rules:department:TCCB";
        _podBCache.Set(cacheKey, "unrelated", TimeSpan.FromMinutes(10));

        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(ChannelName), RedisValue.EmptyString);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        _podBCache.TryGetValue(cacheKey, out _).Should().BeTrue(
            "empty message must be ignored — unrelated keys preserved");
    }

    // Simple polling helper — avoid adding Polly just for one test.
    private static async Task<bool> PollUntilAsync(
        Func<bool> predicate, TimeSpan timeout, TimeSpan pollInterval)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (predicate()) return true;
            await Task.Delay(pollInterval);
        }
        return predicate();
    }
}
