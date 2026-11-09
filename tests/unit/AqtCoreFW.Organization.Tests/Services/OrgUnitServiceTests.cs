using AqtCoreFW.Organization.Application.DTOs;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Organization.Tests.Services;

/// <summary>
/// Unit tests for OrgUnitService.
/// OrgUnit is T2: GetAncestorsAsync accepts/returns PublicId (Guid).
/// TC-ORG-014: GetTreeAsync returns cached result on second call (cache-aside).
/// TC-ORG-015: GetAncestorsAsync walks ancestor chain to root.
/// TC-ORG-016: GetAncestorsAsync stops at max depth 20.
/// </summary>
public sealed class OrgUnitServiceTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly OrgDbContext _db;
    private readonly ICacheService _cache;
    private readonly IReadDbConnection _readDb;
    private readonly OrgUnitService _sut;
    private readonly IGuidGenerator _gen;

    public OrgUnitServiceTests()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)TenantId);
        tenantCtx.IsSystemAdmin.Returns(true);

        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new OrgDbContext(options, tenantCtx);
        _cache = Substitute.For<ICacheService>();
        _readDb = Substitute.For<IReadDbConnection>();
        _sut = new OrgUnitService(_db, _readDb, _cache);

        _gen = Substitute.For<IGuidGenerator>();
        _gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
    }

    public void Dispose() => _db.Dispose();

    // Helper: create and persist an OrgUnit
    private async Task<OrgUnit> SeedAsync(string name, string code, long? parentId = null, int level = 1)
    {
        var unit = OrgUnit.Create(_gen, name, name + " EN", code, TenantId, parentId, level);
        _db.OrgUnits.Add(unit);
        await _db.SaveChangesAsync();
        return unit;
    }

    // --- TC-ORG-014: GetTreeAsync cache-aside ---

    [Fact]
    public async Task GetTreeAsync_FirstCall_QueriesDbAndPopulatesCache()
    {
        _cache.GetAsync<List<OrgUnitDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns((List<OrgUnitDto>?)null);

        await SeedAsync("Ministry", "MIN");

        var result = await _sut.GetTreeAsync(TenantId);

        result.Should().HaveCount(1);
        await _cache.Received(1).SetAsync(
            Arg.Is<string>(k => k.Contains(TenantId.ToString())),
            Arg.Any<List<OrgUnitDto>>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTreeAsync_SecondCall_ReturnsCachedResultWithoutDbQuery()
    {
        var cachedList = new List<OrgUnitDto>
        {
            new(Guid.NewGuid(), null, "Cached Ministry", "Cached EN", "MIN-C", 1, true, TenantId, null)
        };

        _cache.GetAsync<List<OrgUnitDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(
                  _ => Task.FromResult<List<OrgUnitDto>?>(null),       // first call: miss
                  _ => Task.FromResult<List<OrgUnitDto>?>(cachedList)  // second call: hit
              );

        await _sut.GetTreeAsync(TenantId);
        var secondResult = await _sut.GetTreeAsync(TenantId);

        secondResult.Should().HaveCount(1);
        secondResult[0].Name.Should().Be("Cached Ministry");
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(),
            Arg.Any<List<OrgUnitDto>>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTreeAsync_CacheHit_ReturnsWithoutCallingSet()
    {
        var cached = new List<OrgUnitDto>
        {
            new(Guid.NewGuid(), null, "Cached", "Cached EN", "C1", 1, true, TenantId, null)
        };

        _cache.GetAsync<List<OrgUnitDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<List<OrgUnitDto>?>(cached));

        var result = await _sut.GetTreeAsync(TenantId);

        result.Should().BeSameAs(cached);
        await _cache.DidNotReceive().SetAsync(
            Arg.Any<string>(),
            Arg.Any<List<OrgUnitDto>>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<CancellationToken>());
    }

    // --- TC-ORG-015: GetAncestorsAsync walks to root ---

    [Fact]
    public async Task GetAncestorsAsync_ThreeLevelChain_ReturnsChainFromLeafToRoot()
    {
        // OrgUnit is T2: after SaveChanges, entity.Id = long (DB-assigned), entity.PublicId = Guid.
        // Each child's parentId is the parent's long Id; GetAncestorsAsync takes/returns PublicId.
        var root = await SeedAsync("Ministry", "MIN", null, 1);
        var dept = await SeedAsync("Department", "DEPT", root.Id, 2);
        var div  = await SeedAsync("Division", "DIV", dept.Id, 3);

        // GetAncestorsAsync uses GetTreeAsync internally — return null to force DB read
        _cache.GetAsync<List<OrgUnitDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns((List<OrgUnitDto>?)null);

        // Pass div's PublicId — walk returns [div.PublicId, dept.PublicId, root.PublicId]
        var ancestors = await _sut.GetAncestorsAsync(div.PublicId, TenantId);

        ancestors.Should().HaveCount(3);
        ancestors[0].Should().Be(div.PublicId);
        ancestors[1].Should().Be(dept.PublicId);
        ancestors[2].Should().Be(root.PublicId);
    }

    [Fact]
    public async Task GetAncestorsAsync_RootUnit_ReturnsSingleElementList()
    {
        var root = await SeedAsync("Root", "ROOT", null, 1);

        _cache.GetAsync<List<OrgUnitDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns((List<OrgUnitDto>?)null);

        var ancestors = await _sut.GetAncestorsAsync(root.PublicId, TenantId);

        ancestors.Should().ContainSingle().Which.Should().Be(root.PublicId);
    }

    [Fact]
    public async Task GetAncestorsAsync_UnknownId_ReturnsEmptyList()
    {
        _cache.GetAsync<List<OrgUnitDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns((List<OrgUnitDto>?)null);

        var ancestors = await _sut.GetAncestorsAsync(Guid.NewGuid(), TenantId);

        ancestors.Should().BeEmpty();
    }

    // --- TC-ORG-016: GetAncestorsAsync stops at max depth 20 ---

    [Fact]
    public async Task GetAncestorsAsync_ChainOf25_StopsAtDepth20()
    {
        // Inject 25 nodes via cache (all Guid PublicIds); walk stops at 20 iterations.
        var nodes = new List<OrgUnitDto>();
        Guid? currentParent = null;

        for (int i = 1; i <= 25; i++)
        {
            var id = Guid.NewGuid();
            nodes.Add(new OrgUnitDto(id, currentParent, $"Level {i}", $"Level {i} EN", $"L{i:D2}", i, true, TenantId, null));
            currentParent = id;
        }

        _cache.GetAsync<List<OrgUnitDto>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<List<OrgUnitDto>?>(nodes));

        var deepestId = nodes[^1].Id; // node at depth 25
        var ancestors = await _sut.GetAncestorsAsync(deepestId, TenantId);

        // Guard: for (var depth = 0; depth < 20 ...) — max 20 iterations
        ancestors.Should().HaveCount(20);
    }
}
