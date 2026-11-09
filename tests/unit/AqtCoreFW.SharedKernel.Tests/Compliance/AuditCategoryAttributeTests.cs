using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;

namespace AqtCoreFW.SharedKernel.Tests.Compliance;

/// <summary>
/// Unit tests for <see cref="AuditCategoryAttribute"/> + <see cref="AuditCategory"/>.
/// Covers attribute usage flags (class-target, single-instance, inherited),
/// reflection retrieval, and enum ordinal stability (byte-encoded — values feed
/// the AuditLogEntry persistence column directly so ordinals must not drift).
/// </summary>
public sealed class AuditCategoryAttributeTests
{
    [Theory]
    [InlineData(AuditCategory.AppAccess)]
    [InlineData(AuditCategory.AdminLogin)]
    [InlineData(AuditCategory.Error)]
    [InlineData(AuditCategory.AccountManagement)]
    [InlineData(AuditCategory.ConfigChange)]
    public void Attribute_stores_category(AuditCategory category)
    {
        var attr = new AuditCategoryAttribute(category);
        attr.Category.Should().Be(category);
    }

    [Fact]
    public void Attribute_targets_class_single_instance_inherited()
    {
        var usage = typeof(AuditCategoryAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
        usage.AllowMultiple.Should().BeFalse();
        usage.Inherited.Should().BeTrue();
    }

    [Fact]
    public void Enum_ordinals_match_qd742_groups_byte_encoded()
    {
        ((byte)AuditCategory.AppAccess).Should().Be(1);
        ((byte)AuditCategory.AdminLogin).Should().Be(2);
        ((byte)AuditCategory.Error).Should().Be(3);
        ((byte)AuditCategory.AccountManagement).Should().Be(4);
        ((byte)AuditCategory.ConfigChange).Should().Be(5);

        Enum.GetUnderlyingType(typeof(AuditCategory)).Should().Be(typeof(byte));
    }

    [Fact]
    public void Attribute_can_be_read_via_reflection_on_command()
    {
        var attr = typeof(SampleConfigCommand)
            .GetCustomAttributes(typeof(AuditCategoryAttribute), inherit: true)
            .Cast<AuditCategoryAttribute>()
            .SingleOrDefault();

        attr.Should().NotBeNull();
        attr!.Category.Should().Be(AuditCategory.ConfigChange);
    }

    [Fact]
    public void Attribute_inherits_from_base_command_when_subclass_unmarked()
    {
        var attr = typeof(InheritedSampleCommand)
            .GetCustomAttributes(typeof(AuditCategoryAttribute), inherit: true)
            .Cast<AuditCategoryAttribute>()
            .SingleOrDefault();

        attr.Should().NotBeNull();
        attr!.Category.Should().Be(AuditCategory.AccountManagement);
    }

    [Fact]
    public void Attribute_absent_returns_null()
    {
        var attr = typeof(UnmarkedCommand)
            .GetCustomAttributes(typeof(AuditCategoryAttribute), inherit: true)
            .Cast<AuditCategoryAttribute>()
            .SingleOrDefault();

        attr.Should().BeNull();
    }

    // ── Test fixtures ─────────────────────────────────────────────────────────

    [AuditCategory(AuditCategory.ConfigChange)]
    private sealed class SampleConfigCommand : IBaseCommand { }

    [AuditCategory(AuditCategory.AccountManagement)]
    private abstract class AccountManagementCommandBase : IBaseCommand { }

    private sealed class InheritedSampleCommand : AccountManagementCommandBase { }

    private sealed class UnmarkedCommand : IBaseCommand { }
}
