using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.MasterData.Tests.Services;

/// <summary>
/// Behavior tests for <see cref="AdministrativeUnitService"/> — the Singleton cache
/// that powers VN admin-reform code remapping (merged/dissolved units → current active).
///
/// Each test pins a real branch, not a getter:
///   - BuildCacheAsync multi-hop successor-chain walk + cycle guard (the meaty logic),
///   - GetByLevel level/parent filtering,
///   - GetRegionCodes historical-code grouping,
///   - null-cache graceful degradation before the cache is built.
///
/// Mirrors the SystemParamServiceCacheTests pattern: in-memory EF Core with Singleton
/// lifetime so the service's IServiceScopeFactory read and the test seed share one
/// DbContext instance. A real MemoryCache backs the lookups (no Redis/DB at read time).
///
/// Anti-false-green note: breaking the chain-walk in BuildCacheAsync (e.g. stop after
/// one hop) turns BuildCacheAsync_ResolvesMultiHopSuccessorChain RED; removing the
/// visited-set guard makes the cyclic test hang — both are real teeth.
/// </summary>
public sealed class AdministrativeUnitServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly MasterDataDbContext _db;
    private readonly AdministrativeUnitService _sut;

    public AdministrativeUnitServiceTests()
    {
        var dbName = $"AdminUnitTests_{Guid.NewGuid()}";
        var services = new ServiceCollection();

        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.Empty);
        services.AddSingleton(tenantCtx);

        services.AddDbContext<MasterDataDbContext>(
            opt => opt.UseInMemoryDatabase(dbName),
            contextLifetime: ServiceLifetime.Singleton,
            optionsLifetime: ServiceLifetime.Singleton);

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<MasterDataDbContext>();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _sut = new AdministrativeUnitService(
            memoryCache, scopeFactory, NullLogger<AdministrativeUnitService>.Instance);
    }

    public void Dispose() => _serviceProvider.Dispose();

    private static AdministrativeUnit Unit(
        string code, int level, bool active,
        string? parent = null, string? successor = null) =>
        AdministrativeUnit.Create(code, $"vi-{code}", $"en-{code}", level, parent, successor, active);

    private void Seed(params AdministrativeUnit[] units)
    {
        _db.AdministrativeUnits.AddRange(units);
        _db.SaveChanges();
    }

    [Fact]
    public async Task BuildCacheAsync_ResolvesMultiHopSuccessorChain()
    {
        // A (dissolved → B), B (dissolved → C), C (active). An old code must resolve
        // through every hop to the final active unit, not stop at the first successor.
        Seed(
            Unit("A", level: 1, active: false, successor: "B"),
            Unit("B", level: 1, active: false, successor: "C"),
            Unit("C", level: 1, active: true));

        await _sut.BuildCacheAsync();

        _sut.ResolveActive("A").Should().Be("C", "the chain A→B→C collapses to the active tail");
        _sut.ResolveActive("B").Should().Be("C");
        _sut.ResolveActive("C").Should().Be("C", "an active unit resolves to itself");
    }

    [Fact]
    public async Task BuildCacheAsync_CyclicSuccessorChain_TerminatesDeterministically()
    {
        // Pathological data: A → B → A. The visited-set guard must break the walk;
        // without it BuildCacheAsync would spin forever.
        Seed(
            Unit("A", level: 1, active: false, successor: "B"),
            Unit("B", level: 1, active: false, successor: "A"));

        var act = async () => await _sut.BuildCacheAsync();
        await act.Should().NotThrowAsync();

        // The contract is termination with a stable in-cycle value — not the exact
        // landing node, which is an artifact of where the break lands. Pin the contract.
        _sut.ResolveActive("A").Should().BeOneOf("A", "B");
        _sut.ResolveActive("B").Should().BeOneOf("A", "B");
    }

    [Fact]
    public async Task ResolveActive_CodeNotInSuccessorMap_ReturnsInputUnchanged()
    {
        Seed(Unit("P1", level: 1, active: true));
        await _sut.BuildCacheAsync();

        // Still-active and unknown codes are never remapped.
        _sut.ResolveActive("UNKNOWN").Should().Be("UNKNOWN");
        _sut.ResolveActive("P1").Should().Be("P1");
    }

    [Fact]
    public async Task GetByLevel_FiltersByLevelAndOptionalParent()
    {
        Seed(
            Unit("P1", level: 1, active: true),
            Unit("W1", level: 2, active: true, parent: "P1"),
            Unit("W2", level: 2, active: true, parent: "P1"),
            Unit("W3", level: 2, active: true, parent: "P2"));
        await _sut.BuildCacheAsync();

        _sut.GetByLevel(1).Select(u => u.Code).Should().Equal("P1");
        _sut.GetByLevel(2).Should().HaveCount(3);
        _sut.GetByLevel(2, parentCode: "P1").Select(u => u.Code)
            .Should().BeEquivalentTo("W1", "W2");
    }

    [Fact]
    public async Task GetRegionCodes_GroupsHistoricalCodesUnderActiveProvince()
    {
        // Two dissolved provinces merged into the active P_NEW.
        Seed(
            Unit("P_OLD1", level: 1, active: false, successor: "P_NEW"),
            Unit("P_OLD2", level: 1, active: false, successor: "P_NEW"),
            Unit("P_NEW", level: 1, active: true));
        await _sut.BuildCacheAsync();

        // Cross-reform queries on P_NEW must see both legacy codes plus itself.
        _sut.GetRegionCodes("P_NEW").Should().BeEquivalentTo("P_NEW", "P_OLD1", "P_OLD2");
    }

    [Fact]
    public async Task GetRegionCodes_UnknownProvince_ReturnsSelfSingleton()
    {
        Seed(Unit("P1", level: 1, active: true));
        await _sut.BuildCacheAsync();

        _sut.GetRegionCodes("ZZ").Should().BeEquivalentTo("ZZ");
    }

    [Fact]
    public void Lookups_BeforeCacheBuilt_DegradeGracefully()
    {
        // No BuildCacheAsync call → empty cache. The null-coalescing guards must hold
        // so a request hitting the service pre-warm never throws.
        _sut.ResolveActive("X").Should().Be("X");
        _sut.GetByLevel(1).Should().BeEmpty();
        _sut.GetRegionCodes("X").Should().BeEquivalentTo("X");
    }
}
