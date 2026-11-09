using System.Text.Json;
using AqtCoreFW.Audit.Application.DTOs;

namespace AqtCoreFW.Tests.Contracts.Api;

/// <summary>
/// Contract tests for Audit module DTOs — TC-CON-API-010.
/// Ensures compliance-required fields (Law 91/2025) are always present.
/// </summary>
public sealed class AuditApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void AuditLogDto_JsonShape_ComplianceFieldsPresent()
    {
        var dto = new AuditLogDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "admin@gov.vn", "Create", "Cases", "Case",
            Guid.NewGuid().ToString(), "10.0.0.1",
            DateTimeOffset.UtcNow, Guid.NewGuid().ToString());

        var json = JsonSerializer.Serialize(dto);
        var root = JsonDocument.Parse(json).RootElement;

        // Compliance-mandated fields per Law 91/2025
        root.TryGetProperty("Action", out _).Should().BeTrue();
        root.TryGetProperty("UserId", out _).Should().BeTrue();
        root.TryGetProperty("ModuleName", out _).Should().BeTrue();
        root.TryGetProperty("ResourceType", out _).Should().BeTrue();
        root.TryGetProperty("OccurredAt", out _).Should().BeTrue();
        root.TryGetProperty("CorrelationId", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void AuditLogDto_RoundTrip_Lossless()
    {
        var dto = new AuditLogDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "user@gov.vn", "Update", "Forms", "FormTemplate",
            "res-123", "192.168.1.1",
            DateTimeOffset.UtcNow, "corr-456");

        var json = JsonSerializer.Serialize(dto);
        var d = JsonSerializer.Deserialize<AuditLogDto>(json);

        d.Should().NotBeNull();
        d!.Id.Should().Be(dto.Id);
        d.UserId.Should().Be(dto.UserId);
        d.UserName.Should().Be(dto.UserName);
        d.Action.Should().Be(dto.Action);
        d.ModuleName.Should().Be(dto.ModuleName);
        d.ResourceType.Should().Be(dto.ResourceType);
        d.ResourceId.Should().Be(dto.ResourceId);
        d.IpAddress.Should().Be(dto.IpAddress);
        d.CorrelationId.Should().Be(dto.CorrelationId);
    }
}
