using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests for AttributeRuleRepository defence-in-depth guards.
/// Verifies: cross-tenant AddAsync/UpdateAsync rejection.
/// </summary>
public sealed class AttributeRuleRepositoryTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static IdentityDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options);
    }

    [Fact]
    public async Task AddAsync_WrongTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);

        await using var db = CreateInMemoryDbContext();
        var logger = Substitute.For<ILogger<AttributeRuleRepository>>();
        var sut = new AttributeRuleRepository(db, tenantContext, logger);

        // Rule belongs to TenantB, but context is TenantA
        var rule = AttributeRule.Create("Case", "Read", "department", "HR", AttributeEffect.Allow, TenantB);

        // Act
        var act = () => sut.AddAsync(rule, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant*");
    }

    [Fact]
    public async Task UpdateAsync_WrongTenant_ThrowsInvalidOperationException()
    {
        // Arrange
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);

        await using var db = CreateInMemoryDbContext();
        var logger = Substitute.For<ILogger<AttributeRuleRepository>>();
        var sut = new AttributeRuleRepository(db, tenantContext, logger);

        // Rule belongs to TenantB, but context is TenantA
        var rule = AttributeRule.Create("Case", "Read", "department", "HR", AttributeEffect.Allow, TenantB);

        // Act
        var act = () => sut.UpdateAsync(rule, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant*");
    }

    [Fact]
    public async Task AddAsync_GlobalRule_Succeeds()
    {
        // Arrange — global rule (TenantId == null) should be allowed from any tenant context
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);

        await using var db = CreateInMemoryDbContext();
        var logger = Substitute.For<ILogger<AttributeRuleRepository>>();
        var sut = new AttributeRuleRepository(db, tenantContext, logger);

        // Global rule (TenantId == null)
        var rule = AttributeRule.Create("Case", "Read", "role", "Admin", AttributeEffect.Allow, null);

        // Act
        var act = () => sut.AddAsync(rule, CancellationToken.None);

        // Assert — should NOT throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AddAsync_SameTenant_Succeeds()
    {
        // Arrange — rule belongs to same tenant as context
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);

        await using var db = CreateInMemoryDbContext();
        var logger = Substitute.For<ILogger<AttributeRuleRepository>>();
        var sut = new AttributeRuleRepository(db, tenantContext, logger);

        var rule = AttributeRule.Create("Case", "Read", "department", "HR", AttributeEffect.Allow, TenantA);

        // Act
        var act = () => sut.AddAsync(rule, CancellationToken.None);

        // Assert — should NOT throw
        await act.Should().NotThrowAsync();
    }
}
