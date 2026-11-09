using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.Rules.Infrastructure.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Rules.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for DecisionTableRepository defence-in-depth guards.
/// Verifies: cross-tenant read filter (with SystemAdmin bypass) + strict write guards (no admin bypass).
/// </summary>
public sealed class DecisionTableRepositoryTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    // T1 FK: DecisionTable.RuleSetId is long (FK to RuleSet.Id internal PK).
    private const long RuleSetId = 1L;
    private static readonly Guid CreatedBy = Guid.NewGuid();

    /// <summary>
    /// Concrete ITenantContext stub for EF global filter compatibility.
    /// NSubstitute mocks don't work with EF Expression.Constant capture.
    /// </summary>
    private sealed class TestTenantContext(Guid? tenantId, bool isSystemAdmin) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsSystemAdmin { get; } = isSystemAdmin;
    }

    private static RulesDbContext CreateInMemoryDbContext(ITenantContext tenantContext, string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            // Disable model caching so each context uses its own ITenantContext in global filters
            .EnableServiceProviderCaching(false)
            .Options;
        return new RulesDbContext(options, tenantContext);
    }

    private static DecisionTable CreateDecisionTable(Guid tenantId) =>
        DecisionTable.Create(RuleSetId, "Test Table", "[]", "[]", "[]", tenantId, CreatedBy);

    [Fact]
    public async Task GetByIdAsync_SameTenant_ReturnsDecisionTable()
    {
        // Arrange — context has TenantA, DecisionTable belongs to TenantA
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: false);

        await using var db = CreateInMemoryDbContext(tenantContext);
        var logger = Substitute.For<ILogger<DecisionTableRepository>>();
        var sut = new DecisionTableRepository(db, tenantContext, logger);

        var table = CreateDecisionTable(TenantA);
        db.DecisionTables.Add(table);
        await db.SaveChangesAsync();

        // Act — T1 entity: addressed by long Id (IDENTITY assigned by EF on SaveChanges)
        var result = await sut.GetByIdAsync(table.Id);

        // Assert — same tenant = visible
        result.Should().NotBeNull();
        result!.Id.Should().Be(table.Id);
    }

    [Fact]
    public async Task GetByIdAsync_CrossTenant_ReturnsNull()
    {
        // Arrange — DecisionTable belongs to TenantB, context has TenantA
        var dbName = Guid.NewGuid().ToString();

        // Seed with TenantB context
        var seedContext = new TestTenantContext(TenantB, isSystemAdmin: false);
        long tableId;
        await using (var seedDb = CreateInMemoryDbContext(seedContext, dbName))
        {
            var table = CreateDecisionTable(TenantB);
            seedDb.DecisionTables.Add(table);
            await seedDb.SaveChangesAsync();
            tableId = table.Id;
        }

        // Query with TenantA context (cross-tenant)
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: false);
        await using var db = CreateInMemoryDbContext(tenantContext, dbName);

        var logger = Substitute.For<ILogger<DecisionTableRepository>>();
        var sut = new DecisionTableRepository(db, tenantContext, logger);

        // Act
        var result = await sut.GetByIdAsync(tableId);

        // Assert — cross-tenant should return null
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_SystemAdmin_BypassesFilter()
    {
        // Arrange — DecisionTable belongs to TenantB, but context is SystemAdmin with TenantA
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: true);

        await using var db = CreateInMemoryDbContext(tenantContext);
        var logger = Substitute.For<ILogger<DecisionTableRepository>>();
        var sut = new DecisionTableRepository(db, tenantContext, logger);

        // DecisionTable belongs to TenantB (different from context's TenantA)
        var table = CreateDecisionTable(TenantB);
        db.DecisionTables.Add(table);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetByIdAsync(table.Id);

        // Assert — SystemAdmin bypasses inline filter, should return the DecisionTable
        result.Should().NotBeNull();
        result!.Id.Should().Be(table.Id);
    }

    [Fact]
    public async Task AddAsync_WrongTenant_ThrowsInvalidOperationException()
    {
        // Arrange — context has TenantA, but DecisionTable belongs to TenantB
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: false);

        await using var db = CreateInMemoryDbContext(tenantContext);
        var logger = Substitute.For<ILogger<DecisionTableRepository>>();
        var sut = new DecisionTableRepository(db, tenantContext, logger);

        var table = CreateDecisionTable(TenantB);

        // Act
        var act = () => sut.AddAsync(table, CancellationToken.None);

        // Assert — cross-tenant create should throw
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant*");
    }

    [Fact]
    public async Task UpdateAsync_WrongTenant_ThrowsInvalidOperationException()
    {
        // Arrange — context has TenantA, but DecisionTable belongs to TenantB
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: false);

        await using var db = CreateInMemoryDbContext(tenantContext);
        var logger = Substitute.For<ILogger<DecisionTableRepository>>();
        var sut = new DecisionTableRepository(db, tenantContext, logger);

        var table = CreateDecisionTable(TenantB);

        // Act
        var act = () => sut.UpdateAsync(table, CancellationToken.None);

        // Assert — cross-tenant update should throw
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant*");
    }
}
