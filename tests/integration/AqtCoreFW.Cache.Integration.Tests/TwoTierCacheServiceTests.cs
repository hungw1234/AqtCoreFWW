using AqtCoreFW.Cache.Integration.Tests.Fixtures;
using AqtCoreFW.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AqtCoreFW.Cache.Integration.Tests;

/// <summary>
/// Integration tests for TwoTierCacheService against a live Redis Testcontainer.
/// TC-CACHE-INT-001 through TC-CACHE-INT-005.
/// All tests share a single Redis container via RedisCollection fixture (cold-start once).
/// </summary>
[Collection(RedisCollection.CollectionName)]
public sealed class TwoTierCacheServiceTests(RedisFixture redis)
{
    // Unique key prefix per test run — prevents cross-test pollution
    private static string Key(string suffix) => $"test:{Guid.NewGuid():N}:{suffix}";

    // Simple DTO exercised in round-trip tests
    private sealed record CachePayload(Guid Id, string Name, int Value);

    // -------------------------------------------------------------------------
    // TC-CACHE-INT-001: SetAsync/GetAsync round-trips object through Redis
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-CACHE-INT-001")]
    public async Task SetAsync_ThenGetAsync_ReturnsOriginalObject()
    {
        var key = Key("roundtrip");
        var payload = new CachePayload(Guid.NewGuid(), "Integration Test", 42);

        await redis.CacheService.SetAsync(key, payload);
        var result = await redis.CacheService.GetAsync<CachePayload>(key);

        result.Should().NotBeNull();
        result!.Id.Should().Be(payload.Id);
        result.Name.Should().Be(payload.Name);
        result.Value.Should().Be(payload.Value);
    }

    [Fact]
    [Trait("TestCase", "TC-CACHE-INT-001")]
    public async Task SetAsync_ValuePersistedInRedis_DirectRedisReadConfirmsStorage()
    {
        var key = Key("redis-direct");
        var payload = new CachePayload(Guid.NewGuid(), "DirectCheck", 99);

        await redis.CacheService.SetAsync(key, payload);

        // Verify key exists in Redis directly (not via L1 cache)
        var db = redis.Multiplexer.GetDatabase();
        var raw = await db.StringGetAsync(key);
        raw.IsNullOrEmpty.Should().BeFalse(because: "value should be persisted in Redis L2");
    }

    // -------------------------------------------------------------------------
    // TC-CACHE-INT-002: TTL expiration — set with short TTL, wait, assert null
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-CACHE-INT-002")]
    public async Task SetAsync_WithShortTtl_SetsRedisTtlWithJitterFloor()
    {
        var key = Key("ttl-expiry");
        var payload = new CachePayload(Guid.NewGuid(), "Expiry Test", 1);

        // Set with 2-second TTL — AddJitter enforces 1-minute minimum floor
        await redis.CacheService.SetAsync(key, payload, TimeSpan.FromSeconds(2));

        // Confirm value is present
        var before = await redis.CacheService.GetAsync<CachePayload>(key);
        before.Should().NotBeNull(because: "value should be cached immediately after set");

        // Verify Redis TTL was set (jitter floor clamps short TTLs to ~1 minute)
        var db = redis.Multiplexer.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull(because: "Redis key should have a TTL set");
        ttl!.Value.Should().BeGreaterThan(TimeSpan.Zero, because: "TTL must be positive");
        ttl.Value.Should().BeLessThan(TimeSpan.FromMinutes(2),
            because: "TTL should be within jitter range of the 1-minute floor");
    }

    // -------------------------------------------------------------------------
    // TC-CACHE-INT-003: RemoveAsync deletes key from both L1 and Redis
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-CACHE-INT-003")]
    public async Task RemoveAsync_DeletesKeyFromRedisAndMemoryCache()
    {
        var key = Key("remove");
        var payload = new CachePayload(Guid.NewGuid(), "Remove Me", 7);

        await redis.CacheService.SetAsync(key, payload);

        // Confirm stored
        var before = await redis.CacheService.GetAsync<CachePayload>(key);
        before.Should().NotBeNull();

        await redis.CacheService.RemoveAsync(key);

        // L2 (Redis) must be gone
        var db = redis.Multiplexer.GetDatabase();
        var raw = await db.StringGetAsync(key);
        raw.IsNullOrEmpty.Should().BeTrue(because: "Redis key should be deleted by RemoveAsync");

        // GetAsync must now return null (no L1 or L2 hit)
        var after = await redis.CacheService.GetAsync<CachePayload>(key);
        after.Should().BeNull(because: "key should be absent from both cache tiers after removal");
    }

    // -------------------------------------------------------------------------
    // TC-CACHE-INT-004: L1 memory cache serves without Redis hit
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-CACHE-INT-004")]
    public async Task GetAsync_WhenL1Populated_ServesWithoutRedisAccess()
    {
        var key = Key("l1-hit");
        var payload = new CachePayload(Guid.NewGuid(), "L1 Hit", 100);

        // SetAsync populates both L1 and L2
        await redis.CacheService.SetAsync(key, payload);

        // Delete the Redis key directly — L1 still holds the value
        var db = redis.Multiplexer.GetDatabase();
        await db.KeyDeleteAsync(key);

        // GetAsync should return the value from L1 (not Redis)
        var result = await redis.CacheService.GetAsync<CachePayload>(key);

        result.Should().NotBeNull(because: "L1 memory cache should serve the value without hitting Redis");
        result!.Id.Should().Be(payload.Id);
    }

    // -------------------------------------------------------------------------
    // TC-CACHE-INT-005: Handles Redis unavailability gracefully (no exception)
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-CACHE-INT-005")]
    public async Task GetAsync_WhenRedisUnavailable_DoesNotThrow()
    {
        // Wire a separate CacheService against a bad Redis endpoint (not the test container)
        // abortConnect=false means ConnectAsync returns quickly without exception
        var badConfig = ConfigurationOptions.Parse("localhost:19999,abortConnect=false,connectTimeout=100");
        var badMultiplexer = await ConnectionMultiplexer.ConnectAsync(badConfig);

        // Use a fresh MemoryCache — empty, so L1 miss forces L2 attempt
        using var emptyL1 = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        using var degradedService = new TwoTierCacheService(
            emptyL1,
            badMultiplexer,
            Options.Create(new CacheOptions()),
            NullLogger<TwoTierCacheService>.Instance);

        // GetAsync must not throw — returns default on Redis failure
        var act = async () => await degradedService.GetAsync<CachePayload>("test:degraded");
        await act.Should().NotThrowAsync(because: "Redis degradation should be swallowed and return null");

        // SetAsync must not throw — logs warning, stores L1 only
        var actSet = async () =>
            await degradedService.SetAsync("test:degraded-set", new CachePayload(Guid.NewGuid(), "x", 0));
        await actSet.Should().NotThrowAsync(because: "Redis degradation on SetAsync should be swallowed");

        await badMultiplexer.CloseAsync();
    }
}
