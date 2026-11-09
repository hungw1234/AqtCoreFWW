using System.Text.Json;
using AqtCoreFW.Organization.Application.DTOs;

namespace AqtCoreFW.Tests.Contracts.Api;

/// <summary>
/// Contract tests for Organization module DTOs — TC-CON-API-008.
/// </summary>
public sealed class OrganizationApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void OrgUnitDto_JsonShape_AllHierarchyFieldsPresent()
    {
        var dto = new OrgUnitDto(
            Guid.NewGuid(), Guid.NewGuid(), "Phòng CNTT",
            "IT Department", "CNTT", 2, true, Guid.NewGuid(), null);

        var json = JsonSerializer.Serialize(dto);
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("Id", out _).Should().BeTrue();
        root.TryGetProperty("ParentId", out _).Should().BeTrue();
        root.TryGetProperty("Name", out _).Should().BeTrue();
        root.TryGetProperty("NameEn", out _).Should().BeTrue();
        root.TryGetProperty("Code", out _).Should().BeTrue();
        root.TryGetProperty("Level", out _).Should().BeTrue();
        root.TryGetProperty("IsActive", out _).Should().BeTrue();
        root.TryGetProperty("TenantId", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void OrgUnitDto_RoundTrip_Lossless()
    {
        var dto = new OrgUnitDto(
            Guid.NewGuid(), null, "Sở KH-ĐT", "Dept of Planning",
            "SOKHDT", 1, true, Guid.NewGuid(), Guid.NewGuid());

        var json = JsonSerializer.Serialize(dto);
        var d = JsonSerializer.Deserialize<OrgUnitDto>(json);

        d.Should().NotBeNull();
        d!.Code.Should().Be(dto.Code);
        d.Level.Should().Be(dto.Level);
        d.SuccessorId.Should().Be(dto.SuccessorId);
    }
}
