using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SystemParams.Domain.Entities;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace AqtCoreFW.SystemParams.Tests.Services;

/// <summary>
/// TC-SP-D002: SystemParamService.GetValue returns cached value on repeat call.
/// TC-SP-D003: SystemParamService.SetValue updates and invalidates cache.
///
/// Uses in-memory EF Core (Singleton scope) so scope-factory reads and test seeds
/// share the same DbContext instance — same pattern as FeatureFlagServiceTests.
/// ICacheService and IConnectionMultiplexer are substituted to avoid Redis dependency.
/// </summary>
public sealed class SystemParamServiceCacheTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly SystemParamsDbContext _db;
    private readonly ICacheService _cache;
    private readonly SystemParamService _sut;

    public SystemParamServiceCacheTests()
    {
        var dbName = $"SystemParamTests_{Guid.NewGuid()}";
        var services = new ServiceCollection();

        var tenantCtx = Substitute.For<AqtCoreFW.SharedKernel.Domain.ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);
        services.AddSingleton(tenantCtx);

        services.AddDbContext<SystemParamsDbContext>(
            opt => opt.UseInMemoryDatabase(dbName),
            contextLifetime: ServiceLifetime.Singleton,
            optionsLifetime: ServiceLifetime.Singleton);

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<SystemParamsDbContext>();
        _cache = Substitute.For<ICacheService>();

        // Redis substitute — publish returns 0 (no subscribers), no exception
        var redis = Substitute.For<IConnectionMultiplexer>();
        var subscriber = Substitute.For<ISubscriber>();
        redis.GetSubscriber(Arg.Any<object?>()).Returns(subscriber);
        subscriber.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(0L));

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        _sut = new SystemParamService(
            scopeFactory, _cache, redis,
            NullLogger<SystemParamService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private void SeedParam(string key, string value,
        SystemParamDataType dataType = SystemParamDataType.String, string? tenantId = null)
    {
        _db.SystemParameters.Add(
            SystemParameter.Create(key, value, dataType, "test", isEditable: true, tenantId));
        _db.SaveChanges();
    }

    // TC-SP-D002: GetAsync returns cached value on repeat call (cache hit)

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsCachedValueWithoutDbQuery()
    {
        // TC-SP-D002: cache returns value on first call — no DB lookup needed
        _cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("cached-value"));

        var result = await _sut.GetAsync<string>("any:key");

        result.Should().Be("cached-value");
        // DB not seeded — confirms the cached path was used, not DB
    }

    [Fact]
    public async Task GetAsync_CacheMiss_QueriesDbAndPopulatesCache()
    {
        // TC-SP-D002: cache miss → DB read → cache populated
        SeedParam("app:name", "MyApp");
        _cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null)); // cache miss

        var result = await _sut.GetAsync<string>("app:name");

        result.Should().Be("MyApp");
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_UnknownKey_ThrowsKeyNotFoundException()
    {
        _cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        var act = async () => await _sut.GetAsync<string>("nonexistent:key");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // TC-SP-D003: SetAsync updates DB and invalidates cache

    [Fact]
    public async Task SetAsync_NewKey_CreatesParamAndInvalidatesCache()
    {
        // TC-SP-D003: SetAsync creates new param and removes cache entry
        await _sut.SetAsync("new:param", "hello");

        var stored = _db.SystemParameters.FirstOrDefault(p => p.Key == "new:param");
        stored.Should().NotBeNull();
        stored!.Value.Should().Be("hello");

        await _cache.Received(1).RemoveAsync(
            Arg.Is<string>(k => k.Contains("new:param")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAsync_ExistingKey_UpdatesValueAndInvalidatesCache()
    {
        // TC-SP-D003: SetAsync updates existing param and invalidates
        SeedParam("app:timeout", "30", SystemParamDataType.Int);

        await _sut.SetAsync("app:timeout", 60);

        var stored = _db.SystemParameters.First(p => p.Key == "app:timeout");
        stored.Value.Should().Be("60");

        await _cache.Received(1).RemoveAsync(
            Arg.Is<string>(k => k.Contains("app:timeout")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrDefaultAsync_KeyNotFound_ReturnsDefault()
    {
        _cache.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        var result = await _sut.GetOrDefaultAsync("missing:key", "fallback");

        result.Should().Be("fallback");
    }
}
