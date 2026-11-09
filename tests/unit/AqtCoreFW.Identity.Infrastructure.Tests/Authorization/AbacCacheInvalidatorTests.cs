using AqtCoreFW.Identity.Infrastructure.Authorization;
using AqtCoreFW.SharedKernel.Application.Caching;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Authorization;

/// <summary>
/// Unit tests cho AbacCacheInvalidator (T4-05 Phase 01).
/// Verify: (1) key format tenant-aware, (2) cache + publish invoked correctly,
/// (3) Redis publish fail → warning logged, no throw (graceful degradation).
/// </summary>
public sealed class AbacCacheInvalidatorTests
{
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly ISubscriber _subscriber = Substitute.For<ISubscriber>();
    private readonly ILogger<AbacCacheInvalidator> _logger = Substitute.For<ILogger<AbacCacheInvalidator>>();
    private readonly AbacCacheInvalidator _sut;

    public AbacCacheInvalidatorTests()
    {
        _redis.GetSubscriber().Returns(_subscriber);
        _sut = new AbacCacheInvalidator(_cache, _redis, _logger);
    }

    [Fact]
    public async Task InvalidateAsync_SystemScope_UsesSysKey()
    {
        // tenantId null → key = "sys:abac:rules:department:{value}"
        await _sut.InvalidateAsync(tenantId: null, attributeValue: "CNTT");

        await _cache.Received(1).RemoveAsync(
            Arg.Is<string>(k => k == "sys:abac:rules:department:CNTT"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_TenantScope_UsesTenantKey()
    {
        var tenantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        await _sut.InvalidateAsync(tenantId, "TCCB");

        await _cache.Received(1).RemoveAsync(
            Arg.Is<string>(k => k == $"t:{tenantId}:abac:rules:department:TCCB"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_PublishesCrossPodSignal()
    {
        // Publish MUST be called sau cache.RemoveAsync để notify pods khác evict L1.
        await _sut.InvalidateAsync(tenantId: null, attributeValue: "CNTT");

        await _subscriber.Received(1).PublishAsync(
            Arg.Is<RedisChannel>(c => c == RedisChannel.Literal("abac-invalidate")),
            Arg.Is<RedisValue>(v => v == "sys:abac:rules:department:CNTT"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task InvalidateAsync_RedisPublishThrows_DoesNotPropagateException()
    {
        // Graceful degradation: nếu Redis publish fail → log warning, KHÔNG throw.
        // L2 đã evict ở step trước; L1 khác pod sẽ expire tự nhiên.
        _subscriber
            .When(s => s.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>()))
            .Do(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "boom"));

        Func<Task> act = () => _sut.InvalidateAsync(null, "X");

        await act.Should().NotThrowAsync();
        // Cache eviction vẫn phải xảy ra (step 1 trước exception ở step 2).
        await _cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
