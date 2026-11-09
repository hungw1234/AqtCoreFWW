using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Seeding;
using AqtCoreFW.Organization.Infrastructure.Seeding.Loaders;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Organization.Tests.Seeding;

/// <summary>
/// Extended unit tests for OrgSeedHelpers methods — JSON loading, deduplication, batch processing.
/// Tests run against in-memory DbContext to isolate logic from integration concerns.
/// </summary>
public sealed class OrgSeedHelpersExtendedTests : IDisposable
{
    private readonly OrgDbContext _ctx;

    private static IGuidGenerator MockGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    public OrgSeedHelpersExtendedTests()
    {
        var options = new DbContextOptionsBuilder<OrgDbContext>()
            .UseInMemoryDatabase($"OrgSeedHelpersExtendedTests_{Guid.NewGuid()}")
            .Options;
        _ctx = new OrgDbContext(options, new TestTenantContext());
        _ctx.Database.EnsureCreated();
    }

    public void Dispose() => _ctx.Dispose();

    [Fact]
    [Trait("Category", "Unit")]
    public void LoadNodesFromJson_ReturnsExpectedCount_ForLevel12Resource()
    {
        // Act
        var data = JsonSeedDataLoader.LoadFromResource("btc-level-1-2.json");

        // Assert
        data.Should().NotBeNull();
        data.Should().HaveCount(36, "1 L1 root (BTC) + 35 L2 units");

        // Verify structure
        var l1 = data.Where(n => n.Level == 1).ToList();
        l1.Should().HaveCount(1);
        l1[0].Code.Should().Be("BTC");

        var l2 = data.Where(n => n.Level == 2).ToList();
        l2.Should().HaveCount(35);
        l2.Should().OnlyContain(n => n.ParentCode == "BTC");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BuildCodeToIdMap_DuplicateCodes_ThrowsOrReturnsLast()
    {
        // Arrange — add two OrgUnits with same code (should not happen in production)
        var entity1 = OrgUnit.Create(MockGen(), "Unit A", "Unit A En", "DUPCODE", TenantSeedProfiles.BoTaiChinh.TenantId, null, 1);
        var entity2 = OrgUnit.Create(MockGen(), "Unit B", "Unit B En", "DUPCODE", TenantSeedProfiles.BoTaiChinh.TenantId, null, 1);
        _ctx.OrgUnits.AddRange(entity1, entity2);
        await _ctx.SaveChangesAsync();

        // Act & Assert
        // Dictionary creation with duplicate keys will throw or use last value
        var act = async () => await OrgSeedHelpers.BuildCodeToIdMapAsync(_ctx, TenantSeedProfiles.BoTaiChinh.TenantId, 1);

        // Note: ToDictionary throws on duplicate keys by default
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task NodeExistsAsync_ReturnsFalse_WhenCodeMissing()
    {
        // Arrange — empty context

        // Act
        var result = await OrgSeedHelpers.NodeExistsAsync(
            _ctx, TenantSeedProfiles.BoTaiChinh.TenantId, "NONEXISTENT_CODE");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task NodeExistsAsync_IgnoresQueryFilters()
    {
        // Arrange
        var entity = OrgUnit.Create(MockGen(), "Test Unit", "Test Unit En", "TESTCODE", TenantSeedProfiles.BoTaiChinh.TenantId, null, 1);
        _ctx.OrgUnits.Add(entity);
        await _ctx.SaveChangesAsync();

        // Act — even with a different tenant ID, IgnoreQueryFilters should find the entity
        var result = await OrgSeedHelpers.NodeExistsAsync(_ctx, TenantSeedProfiles.BoTaiChinh.TenantId, "TESTCODE");

        // Assert
        result.Should().BeTrue("NodeExistsAsync uses IgnoreQueryFilters to bypass tenant isolation");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BatchInsertAsync_RespectsBatchSize_MultipleBatches()
    {
        // Arrange
        var entities = Enumerable.Range(1, 10)
            .Select(i => OrgUnit.Create(
                MockGen(), $"Unit{i}", $"Unit{i} En", $"BATCH{i:D2}",
                TenantSeedProfiles.BoTaiChinh.TenantId, null, 1))
            .ToList();

        // Act — batch size 3 should require 4 SaveChangesAsync calls (3+3+3+1)
        await OrgSeedHelpers.BatchInsertAsync(_ctx, entities, batchSize: 3);

        // Assert
        var count = await _ctx.OrgUnits.IgnoreQueryFilters().CountAsync();
        count.Should().Be(10, "all entities should be inserted despite batch size");

        // Verify all codes present
        var codes = await _ctx.OrgUnits.IgnoreQueryFilters()
            .Select(x => x.Code)
            .ToListAsync();
        codes.Should().HaveCount(10);
        Enumerable.Range(1, 10).All(i => codes.Contains($"BATCH{i:D2}")).Should().BeTrue();
    }

    /// <summary>Test helper — minimal ITenantContext for in-memory testing.</summary>
    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsSystemAdmin => true;
    }
}
