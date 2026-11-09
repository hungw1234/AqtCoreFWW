using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.Rules.Infrastructure.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Rules.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for RuleSetRepository defence-in-depth guards.
/// Verifies: cross-tenant read filter (with SystemAdmin bypass) + strict write guards (no admin bypass).
/// </summary>
public sealed class RuleSetRepositoryTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
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

    /// <summary>
    /// Creates a RuleSet using IGuidGenerator mock so PublicId is deterministic.
    /// T2 entity: PublicId set at factory time by IGuidGenerator.
    /// </summary>
    private static RuleSet MakeRuleSet(Guid tenantId, string name = "Test", string? desc = "desc")
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(Guid.NewGuid());
        return RuleSet.Create(gen, name, tenantId, CreatedBy, desc);
    }

    [Fact]
    public async Task GetByPublicIdAsync_SameTenant_ReturnsRuleSet()
    {
        // Arrange — context has TenantA, RuleSet belongs to TenantA
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: false);

        await using var db = CreateInMemoryDbContext(tenantContext);
        var logger = Substitute.For<ILogger<RuleSetRepository>>();
        var sut = new RuleSetRepository(db, tenantContext, logger);

        var ruleSet = MakeRuleSet(TenantA);
        db.RuleSets.Add(ruleSet);
        await db.SaveChangesAsync();

        // Act — T2 entity: addressed by PublicId (Guid AK)
        var result = await sut.GetByPublicIdAsync(ruleSet.PublicId);

        // Assert — same tenant = visible
        result.Should().NotBeNull();
        result!.PublicId.Should().Be(ruleSet.PublicId);
    }

    [Fact]
    public async Task GetByPublicIdAsync_CrossTenant_ReturnsNull()
    {
        // Arrange — RuleSet belongs to TenantB, context has TenantA
        var dbName = Guid.NewGuid().ToString();

        // Seed with TenantB context
        var seedContext = new TestTenantContext(TenantB, isSystemAdmin: false);
        Guid ruleSetPublicId;
        await using (var seedDb = CreateInMemoryDbContext(seedContext, dbName))
        {
            var ruleSet = MakeRuleSet(TenantB, "CrossTest");
            seedDb.RuleSets.Add(ruleSet);
            await seedDb.SaveChangesAsync();
            ruleSetPublicId = ruleSet.PublicId;
        }

        // Query with TenantA context (cross-tenant)
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: false);
        await using var db = CreateInMemoryDbContext(tenantContext, dbName);

        var logger = Substitute.For<ILogger<RuleSetRepository>>();
        var sut = new RuleSetRepository(db, tenantContext, logger);

        // Act
        var result = await sut.GetByPublicIdAsync(ruleSetPublicId);

        // Assert — cross-tenant should return null
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByPublicIdAsync_SystemAdmin_BypassesFilter()
    {
        // Arrange — RuleSet belongs to TenantB, but context is SystemAdmin with TenantA.
        // NOTE: EF InMemory has limitations with global filter expression caching.
        // This test seeds and queries with the SAME SystemAdmin context to verify
        // that admin can access cross-tenant data via the inline filter bypass.
        // Full cross-context SystemAdmin behavior is verified in integration tests.
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: true);

        await using var db = CreateInMemoryDbContext(tenantContext);
        var logger = Substitute.For<ILogger<RuleSetRepository>>();
        var sut = new RuleSetRepository(db, tenantContext, logger);

        // RuleSet belongs to TenantB (different from context's TenantA)
        var ruleSet = MakeRuleSet(TenantB, "AdminTest");
        db.RuleSets.Add(ruleSet);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.GetByPublicIdAsync(ruleSet.PublicId);

        // Assert — SystemAdmin bypasses inline filter, should return the RuleSet
        result.Should().NotBeNull();
        result!.PublicId.Should().Be(ruleSet.PublicId);
    }

    [Fact]
    public async Task AddAsync_WrongTenant_ThrowsInvalidOperationException()
    {
        // Arrange — context has TenantA, but RuleSet belongs to TenantB
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: false);

        await using var db = CreateInMemoryDbContext(tenantContext);
        var logger = Substitute.For<ILogger<RuleSetRepository>>();
        var sut = new RuleSetRepository(db, tenantContext, logger);

        var ruleSet = MakeRuleSet(TenantB, "WrongTenant");

        // Act
        var act = () => sut.AddAsync(ruleSet, CancellationToken.None);

        // Assert — cross-tenant create should throw
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant*");
    }

    [Fact]
    public async Task UpdateAsync_WrongTenant_ThrowsInvalidOperationException()
    {
        // Arrange — context has TenantA, but RuleSet belongs to TenantB
        var tenantContext = new TestTenantContext(TenantA, isSystemAdmin: false);

        await using var db = CreateInMemoryDbContext(tenantContext);
        var logger = Substitute.For<ILogger<RuleSetRepository>>();
        var sut = new RuleSetRepository(db, tenantContext, logger);

        var ruleSet = MakeRuleSet(TenantB, "UpdateWrongTenant");

        // Act
        var act = () => sut.UpdateAsync(ruleSet, CancellationToken.None);

        // Assert — cross-tenant update should throw
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant*");
    }
}
