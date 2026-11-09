using AqtCoreFW.MasterData.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.MasterData.Tests.Entities;

/// <summary>
/// TC-MD-P34: ProvinceLegacyMapping entity tests (63->34 province mapping QD 19/2025).
/// </summary>
public sealed class ProvinceLegacyMappingTests
{
    private static readonly DateTimeOffset MergedAt = new(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Create_WithValidData_SetsAllProperties()
    {
        var mapping = ProvinceLegacyMapping.Create("02", "08", MergedAt, "Ha Giang -> Tuyen Quang");

        // Phase-4b: T1 entity — long Id is DB-assigned (0 pre-SaveChanges); Guid.Empty compare removed.
        mapping.OldCode.Should().Be("02");
        mapping.NewCode.Should().Be("08");
        mapping.MergedAt.Should().Be(MergedAt);
        mapping.Notes.Should().Be("Ha Giang -> Tuyen Quang");
        mapping.IsIdentity.Should().BeFalse();
    }

    [Fact]
    public void Create_IdentityMapping_SetsIsIdentityTrue()
    {
        var mapping = ProvinceLegacyMapping.Create("01", "01", MergedAt, "Ha Noi - unchanged");

        mapping.OldCode.Should().Be("01");
        mapping.NewCode.Should().Be("01");
        mapping.IsIdentity.Should().BeTrue();
    }

    [Fact]
    public void Create_WithNullNotes_AllowsNullNotes()
    {
        var mapping = ProvinceLegacyMapping.Create("04", "04", MergedAt);

        mapping.Notes.Should().BeNull();
        mapping.IsIdentity.Should().BeTrue();
    }

    [Fact]
    public void Create_MergedProvince_SetsCorrectMapping()
    {
        // Ha Giang (02) merged into Tuyen Quang (08)
        var mapping = ProvinceLegacyMapping.Create("02", "08", MergedAt, "Ha Giang -> Tuyen Quang");

        mapping.OldCode.Should().NotBe(mapping.NewCode);
        mapping.IsIdentity.Should().BeFalse();
    }

    [Fact]
    public void Create_MultipleIdentityMappings_AllHaveIsIdentityTrue()
    {
        var mappings = new[]
        {
            ProvinceLegacyMapping.Create("01", "01", MergedAt, "Ha Noi"),
            ProvinceLegacyMapping.Create("04", "04", MergedAt, "Cao Bang"),
            ProvinceLegacyMapping.Create("11", "11", MergedAt, "Dien Bien"),
        };

        mappings.Should().AllSatisfy(m => m.IsIdentity.Should().BeTrue());
    }

    [Fact]
    public void Create_MultipleMergedMappings_AllHaveIsIdentityFalse()
    {
        var mappings = new[]
        {
            ProvinceLegacyMapping.Create("02", "08", MergedAt, "Ha Giang -> Tuyen Quang"),
            ProvinceLegacyMapping.Create("06", "19", MergedAt, "Bac Kan -> Thai Nguyen"),
            ProvinceLegacyMapping.Create("15", "15", MergedAt, "Yen Bai -> Lao Cai"),
        };

        // Last one is identity (15 -> 15), first two are not
        mappings[0].IsIdentity.Should().BeFalse();
        mappings[1].IsIdentity.Should().BeFalse();
        mappings[2].IsIdentity.Should().BeTrue(); // 15 -> 15 is identity
    }
}
