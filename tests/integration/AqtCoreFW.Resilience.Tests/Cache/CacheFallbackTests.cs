using AqtCoreFW.Infrastructure.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Xunit;

namespace AqtCoreFW.Resilience.Tests.Cache;

/// <summary>
/// Unit tests for TwoTierCacheService graceful degradation.
/// Redis is mocked to throw RedisException — verifies L1 fallback and null-on-miss behaviour.
/// </summary>
[Trait("Category", "Resilience")]
public sealed class CacheFallbackTests : IDisposable
{
    // ── shared fixtures ────────────────────────────────────────────────────────

    private readonly IMemoryCache _memoryCache;
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _redisDb = Substitute.For<IDatabase>();
    private readonly TwoTierCacheService _sut;

    public CacheFallbackTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });

        _redis.GetDatabase().Returns(_redisDb);

        var opts = Options.Create(new CacheOptions
        {
            L1 = new CacheOptions.L1Options { DefaultTtlMinutes = 10, MaxEntries = 100 },
            L2 = new CacheOptions.L2Options { DefaultTtlMinutes = 60, JitterPercent = 0 }
        });

        _sut = new TwoTierCacheService(
            _memoryCache,
            _redis,
            opts,
            NullLogger<TwoTierCacheService>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _memoryCache.Dispose();
    }

    // ── TC-RES-CACHE-001 ───────────────────────────────────────────────────────

    /// <summary>
    /// TC-RES-CACHE-001: Falls back to L1 memory when Redis throws RedisException.
    /// Value pre-seeded into L1 — should be returned even if Redis is down.
    /// </summary>
    [Fact]
    public async Task GetAsync_RedisUnavailable_ReturnsCachedValueFromL1()
    {
        // Arrange — put value directly into L1 memory cache
        const string key = "tenant-1:entity:42";
        const string expected = "cached-value";
        _memoryCache.Set(key, expected,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                Size = 1
            });

        // Redis throws — should never be reached because L1 hit happens first
        _redisDb.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
                .ThrowsAsync(new RedisException("connection refused"));

        // Act
        var result = await _sut.GetAsync<string>(key);

        // Assert
        result.Should().Be(expected);
    }

    // ── TC-RES-CACHE-002 ───────────────────────────────────────────────────────

    /// <summary>
    /// TC-RES-CACHE-002: Cache miss (both L1 and Redis down) returns null gracefully.
    /// No exception should surface to the caller.
    /// </summary>
    [Fact]
    public async Task GetAsync_BothTiersMiss_ReturnsNullGracefully()
    {
        // Arrange — L1 empty, Redis throws
        const string key = "tenant-1:entity:unknown";

        _redisDb.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
                .ThrowsAsync(new RedisException("timeout"));

        // Act
        var result = await _sut.GetAsync<string>(key);

        // Assert — must not throw; returns null/default
        result.Should().BeNull();
    }

    // ── TC-RES-CACHE-003 ───────────────────────────────────────────────────────

    /// <summary>
    /// TC-RES-CACHE-003: SetAsync with explicit TTL stores value in L1 and attempts L2.
    /// Verifies L1 is readable immediately after Set even when Redis is unavailable.
    /// </summary>
    [Fact]
    public async Task SetAsync_WithTtl_StoresInL1AndDegradesSilentlyOnRedisFailure()
    {
        // Arrange — Redis throws on write
        const string key = "tenant-1:entity:new";
        const string value = "stored-value";
        var ttl = TimeSpan.FromMinutes(30);

        _redisDb.StringSetAsync(
                    Arg.Any<RedisKey>(),
                    Arg.Any<RedisValue>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<bool>(),
                    Arg.Any<When>(),
                    Arg.Any<CommandFlags>())
                .ThrowsAsync(new RedisException("write failed"));

        // Act — should not throw despite Redis failure
        var act = async () => await _sut.SetAsync(key, value, ttl);
        await act.Should().NotThrowAsync();

        // Assert — L1 readable immediately after set
        var stored = await _sut.GetAsync<string>(key);
        stored.Should().Be(value);
    }
}
