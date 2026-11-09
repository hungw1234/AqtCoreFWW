using System.Text.Json;
using AqtCoreFW.Workflow.Domain.Events;

namespace AqtCoreFW.Tests.Contracts.Events;

/// <summary>
/// Contract tests for Workflow module domain events — TC-CON-EVT-007.
/// WorkflowTransitionedEvent consumed by Audit module for NĐ13/Law 91/2025 trail.
/// Phase-4b: InstanceId → InstancePublicId (Guid T2 AK); NewStateId → NewStateInternalId (long T1 PK).
/// </summary>
public sealed class WorkflowEventsContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void WorkflowTransitionedEvent_RoundTrip()
    {
        var evt = new WorkflowTransitionedEvent(
            Guid.NewGuid(), 42L, Guid.NewGuid(), Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var d = JsonSerializer.Deserialize<WorkflowTransitionedEvent>(json);

        d.Should().NotBeNull();
        d!.InstancePublicId.Should().Be(evt.InstancePublicId);
        d.NewStateInternalId.Should().Be(evt.NewStateInternalId);
        d.TransitionedBy.Should().Be(evt.TransitionedBy);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void WorkflowTransitionedEvent_JsonShape_ContainsExpectedProperties()
    {
        var evt = new WorkflowTransitionedEvent(
            Guid.NewGuid(), 1L, Guid.NewGuid(), Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var root = JsonDocument.Parse(json).RootElement;

        root.TryGetProperty("InstancePublicId", out _).Should().BeTrue();
        root.TryGetProperty("NewStateInternalId", out _).Should().BeTrue();
        root.TryGetProperty("TransitionedBy", out _).Should().BeTrue();
        root.TryGetProperty("MessageId", out _).Should().BeTrue();
        root.TryGetProperty("OccurredAt", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void SlaBreachedEvent_RoundTrip()
    {
        var evt = new SlaBreachedEvent(
            Guid.NewGuid(), 7L, Guid.NewGuid(),
            Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-48), 48, Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var d = JsonSerializer.Deserialize<SlaBreachedEvent>(json);

        d.Should().NotBeNull();
        d!.InstancePublicId.Should().Be(evt.InstancePublicId);
        d.TenantId.Should().Be(evt.TenantId);
        d.ElapsedHours.Should().Be(48);
    }
}
