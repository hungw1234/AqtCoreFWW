using AqtCoreFW.Identity.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Domain.Tests.Entities;

/// <summary>
/// Tests for AttributeRule entity encapsulation.
/// Verifies: TenantId setter is private (defence-in-depth).
/// </summary>
public sealed class AttributeRuleTests
{
    [Fact]
    public void TenantIdSetter_IsPrivate()
    {
        // Arrange
        var property = typeof(AttributeRule).GetProperty(nameof(AttributeRule.TenantId));

        // Act
        var setter = property?.GetSetMethod(nonPublic: true);

        // Assert
        property.Should().NotBeNull("AttributeRule should have TenantId property");
        setter.Should().NotBeNull("TenantId should have a setter");
        setter!.IsPrivate.Should().BeTrue("TenantId setter should be private for defence-in-depth");
    }

    [Fact]
    public void Create_SetsTenantIdCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // Act
        var rule = AttributeRule.Create("Case", "Read", "department", "HR", AttributeEffect.Allow, tenantId);

        // Assert — Create() should set TenantId despite private setter
        rule.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void Create_WithNullTenantId_CreatesGlobalRule()
    {
        // Act
        var rule = AttributeRule.Create("Case", "Read", "role", "Admin", AttributeEffect.Allow, null);

        // Assert — global rule has null TenantId
        rule.TenantId.Should().BeNull();
    }
}
