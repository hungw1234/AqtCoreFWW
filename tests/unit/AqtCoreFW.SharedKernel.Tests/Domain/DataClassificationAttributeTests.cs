using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;

namespace AqtCoreFW.SharedKernel.Tests.Domain;

/// <summary>
/// Unit tests for DataClassificationAttribute and DataClassificationLevel.
/// </summary>
public sealed class DataClassificationAttributeTests
{
    [Theory]
    [InlineData(DataClassificationLevel.Public)]
    [InlineData(DataClassificationLevel.Internal)]
    [InlineData(DataClassificationLevel.Confidential)]
    [InlineData(DataClassificationLevel.Restricted)]
    public void Attribute_stores_level_correctly(DataClassificationLevel level)
    {
        var attr = new DataClassificationAttribute(level);
        attr.Level.Should().Be(level);
    }

    [Fact]
    public void Attribute_is_sealed_and_targets_property()
    {
        var attrUsage = typeof(DataClassificationAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        attrUsage.ValidOn.Should().HaveFlag(AttributeTargets.Property);
        attrUsage.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void Enum_values_have_expected_ordinal_order()
    {
        // Restricted must be highest so >= comparisons work correctly
        ((int)DataClassificationLevel.Public).Should().Be(0);
        ((int)DataClassificationLevel.Internal).Should().Be(1);
        ((int)DataClassificationLevel.Confidential).Should().Be(2);
        ((int)DataClassificationLevel.Restricted).Should().Be(3);

        ((int)DataClassificationLevel.Restricted).Should().BeGreaterThan((int)DataClassificationLevel.Confidential);
        ((int)DataClassificationLevel.Confidential).Should().BeGreaterThan((int)DataClassificationLevel.Internal);
        ((int)DataClassificationLevel.Internal).Should().BeGreaterThan((int)DataClassificationLevel.Public);
    }

    [Fact]
    public void Attribute_can_be_applied_to_property_via_reflection()
    {
        var prop = typeof(SampleEntity).GetProperty(nameof(SampleEntity.NationalId))!;
        var attr = prop.GetCustomAttributes(typeof(DataClassificationAttribute), inherit: true)
            .Cast<DataClassificationAttribute>()
            .SingleOrDefault();

        attr.Should().NotBeNull();
        attr!.Level.Should().Be(DataClassificationLevel.Restricted);
    }

    [Fact]
    public void Attribute_not_present_on_unclassified_property()
    {
        var prop = typeof(SampleEntity).GetProperty(nameof(SampleEntity.DisplayName))!;
        var attr = prop.GetCustomAttributes(typeof(DataClassificationAttribute), inherit: true)
            .Cast<DataClassificationAttribute>()
            .SingleOrDefault();

        attr.Should().BeNull();
    }

    // ── Test fixture ─────────────────────────────────────────────────────────

    private sealed class SampleEntity
    {
        [DataClassification(DataClassificationLevel.Restricted)]
        public string NationalId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        [DataClassification(DataClassificationLevel.Confidential)]
        public string Email { get; set; } = string.Empty;
    }
}
