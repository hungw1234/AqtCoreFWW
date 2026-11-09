using System.Text.Json;
using AqtCoreFW.Workflow.Domain.Events;
using FluentAssertions;

namespace AqtCoreFW.Tests.Contracts.Events;

/// <summary>
/// Contract test for SlaBreachedEvent — serialization round-trip stability.
/// Phase-4b: InstanceId → InstancePublicId (Guid); CurrentStateId → CurrentStateInternalId (long);
/// DefinitionId → DefinitionPublicId (Guid).
/// TenantId is Guid (aligns with all other modules).
/// ElapsedHours is int (not double).
/// </summary>
public sealed class SlaBreachedEventContractTests
{
    private static readonly Guid SampleTenantId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    [Trait("Category", "Contract")]
    public void SlaBreachedEvent_RoundTrip()
    {
        var evt = new SlaBreachedEvent(
            InstancePublicId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CurrentStateInternalId: 22L,
            DefinitionPublicId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            TenantId: SampleTenantId,
            StateEnteredAt: DateTimeOffset.Parse("2026-03-17T10:00:00Z"),
            ElapsedHours: 48,
            MessageId: Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var d = JsonSerializer.Deserialize<SlaBreachedEvent>(json);

        d.Should().NotBeNull();
        d!.InstancePublicId.Should().Be(evt.InstancePublicId);
        d.CurrentStateInternalId.Should().Be(evt.CurrentStateInternalId);
        d.DefinitionPublicId.Should().Be(evt.DefinitionPublicId);
        d.TenantId.Should().Be(SampleTenantId);
        d.ElapsedHours.Should().Be(48);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void SlaBreachedEvent_JsonShape_ContainsExpectedProperties()
    {
        var evt = new SlaBreachedEvent(
            Guid.NewGuid(), 1L, Guid.NewGuid(),
            Guid.NewGuid(), DateTimeOffset.UtcNow, 24, Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("InstancePublicId", out _).Should().BeTrue();
        root.TryGetProperty("TenantId", out _).Should().BeTrue();
        root.TryGetProperty("ElapsedHours", out _).Should().BeTrue();
        root.TryGetProperty("StateEnteredAt", out _).Should().BeTrue();
    }
}
