using AqtCoreFW.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace AqtCoreFW.Cache.Integration.Tests.Fixtures;

/// <summary>
/// Spins up a Redis Testcontainers instance shared across the cache test collection.
/// Exposes a fully-wired TwoTierCacheService and raw IConnectionMultiplexer for assertions.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithCleanUp(true)
        .Build();

    public IConnectionMultiplexer Multiplexer { get; private set; } = null!;

    /// <summary>
    /// Fully-wired TwoTierCacheService backed by the live Redis container.
    /// L1 TTL: 10 min (default). L2 TTL: 60 min (default).
    /// </summary>
    public TwoTierCacheService CacheService { get; private set; } = null!;

    /// <summary>
    /// Isolated IMemoryCache instance — use this to assert L1 cache state.
    /// </summary>
    public IMemoryCache MemoryCache { get; private set; } = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        Multiplexer = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        MemoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5000 });

        var cacheOptions = Options.Create(new CacheOptions());
        CacheService = new TwoTierCacheService(
            MemoryCache,
            Multiplexer,
            cacheOptions,
            NullLogger<TwoTierCacheService>.Instance);
    }

    public async Task DisposeAsync()
    {
        CacheService.Dispose();
        MemoryCache.Dispose();
        await Multiplexer.CloseAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(CollectionName)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string CollectionName = "CacheRedis";
}
