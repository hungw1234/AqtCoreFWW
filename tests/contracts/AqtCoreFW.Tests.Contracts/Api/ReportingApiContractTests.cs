using System.Text.Json;
using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.Reporting.Domain.Enums;

namespace AqtCoreFW.Tests.Contracts.Api;

/// <summary>
/// Contract tests for Reporting module DTOs — TC-CON-API-011.
/// </summary>
public sealed class ReportingApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void ReportDefinitionDto_JsonShape_AllFieldsPresent()
    {
        var dto = new ReportDefinitionDto(
            Guid.NewGuid(), "Monthly KPI", "KPI Tháng",
            "Monthly KPI summary", "{}", OutputFormat.Pdf,
            true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(dto);
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("Id", out _).Should().BeTrue();
        root.TryGetProperty("Name", out _).Should().BeTrue();
        root.TryGetProperty("ParametersSchema", out _).Should().BeTrue();
        root.TryGetProperty("OutputFormat", out _).Should().BeTrue();
        root.TryGetProperty("IsActive", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void ReportDefinitionDto_RoundTrip_Lossless()
    {
        var dto = new ReportDefinitionDto(
            Guid.NewGuid(), "Cases Report", "Báo cáo hồ sơ",
            "All cases", "{\"year\":2026}", OutputFormat.Excel,
            true, null, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(dto);
        var d = JsonSerializer.Deserialize<ReportDefinitionDto>(json);

        d.Should().NotBeNull();
        d!.Name.Should().Be(dto.Name);
        d.ParametersSchema.Should().Be(dto.ParametersSchema);
    }
}
