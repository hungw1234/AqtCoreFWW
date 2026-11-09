using System.Text.Json;
using AqtCoreFW.SharedKernel.Contracts.Events;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;

namespace AqtCoreFW.Tests.Contracts.Events;

/// <summary>
/// Contract tests for the outbox-routed integration events (Slice 1B/1D + outbox-hardening):
/// <see cref="FormSubmissionAcknowledgedIntegrationEvent"/>,
/// <see cref="AuditLogAppendedIntegrationEvent"/>, and
/// <see cref="WorkflowInstanceFinalizedIntegrationEvent"/>.
///
/// These are the events actually serialized to <c>OutboxMessages</c> at rest and replayed on
/// dispatch, so their JSON shape must stay stable (a rename silently dead-letters pending rows)
/// and they must round-trip losslessly. They also carry the <see cref="ITenantScopedIntegrationEvent"/>
/// marker (Slice 1D) so <c>TenantScopeConsumeFilter</c> can pin the consume scope to the payload
/// tenant — verified here so the marker cannot be dropped unnoticed.
/// </summary>
public sealed class OutboxIntegrationEventsContractTests
{
    // ── FormSubmissionAcknowledged ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Contract")]
    public void FormSubmissionAcknowledged_Serialization_RoundTrip_IsStable()
    {
        var evt = new FormSubmissionAcknowledgedIntegrationEvent(
            SubmissionId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            FormTemplateId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            TenantId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            SubmittedBy: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            MessageId: Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            OccurredAt: DateTimeOffset.Parse("2026-07-07T10:30:00+07:00"));

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<FormSubmissionAcknowledgedIntegrationEvent>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Should().BeEquivalentTo(evt);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void FormSubmissionAcknowledged_JsonShape_ContainsExpectedProperties()
    {
        var evt = new FormSubmissionAcknowledgedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        var root = JsonDocument.Parse(JsonSerializer.Serialize(evt)).RootElement;

        root.TryGetProperty("SubmissionId", out _).Should().BeTrue();
        root.TryGetProperty("FormTemplateId", out _).Should().BeTrue();
        root.TryGetProperty("TenantId", out _).Should().BeTrue();
        root.TryGetProperty("SubmittedBy", out _).Should().BeTrue();
        root.TryGetProperty("MessageId", out _).Should().BeTrue();
        root.TryGetProperty("OccurredAt", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void FormSubmissionAcknowledged_ExposesPayloadTenant_ViaMarker()
    {
        var tenantId = Guid.NewGuid();
        var evt = new FormSubmissionAcknowledgedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), tenantId, Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        ((ITenantScopedIntegrationEvent)evt).TenantId.Should().Be(tenantId);
    }

    // ── AuditLogAppended ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Contract")]
    public void AuditLogAppended_Serialization_RoundTrip_IsStable()
    {
        var evt = new AuditLogAppendedIntegrationEvent(
            AuditEntryId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TenantId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ActorUserId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Action: "Update",
            ModuleName: "Forms",
            ResourceType: "FormTemplate",
            ResourceId: "template-42",
            MessageId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            OccurredAt: DateTimeOffset.Parse("2026-07-07T10:30:00+07:00"));

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<AuditLogAppendedIntegrationEvent>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Should().BeEquivalentTo(evt);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void AuditLogAppended_NullableFields_SurviveRoundTrip()
    {
        // System audit rows have no tenant / no actor — the nullable fields must round-trip as null.
        var evt = new AuditLogAppendedIntegrationEvent(
            AuditEntryId: Guid.NewGuid(),
            TenantId: null,
            ActorUserId: null,
            Action: "SystemPurge",
            ModuleName: "Audit",
            ResourceType: "RetentionSweep",
            ResourceId: null,
            MessageId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<AuditLogAppendedIntegrationEvent>(json);

        deserialized!.TenantId.Should().BeNull();
        deserialized.ActorUserId.Should().BeNull();
        deserialized.ResourceId.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void AuditLogAppended_ExposesPayloadTenant_ViaMarker()
    {
        var tenantId = Guid.NewGuid();
        var evt = new AuditLogAppendedIntegrationEvent(
            Guid.NewGuid(), tenantId, Guid.NewGuid(), "Read", "Audit", "Log", null,
            Guid.NewGuid(), DateTimeOffset.UtcNow);

        ((ITenantScopedIntegrationEvent)evt).TenantId.Should().Be(tenantId);
    }

    // ── WorkflowInstanceFinalized ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Contract")]
    public void WorkflowInstanceFinalized_Serialization_RoundTrip_IsStable()
    {
        var evt = new WorkflowInstanceFinalizedIntegrationEvent(
            InstancePublicId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            EntityType: "FormSubmission",
            EntityId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            FinalStateName: "Approved",
            TransitionedBy: Guid.Parse("77777777-7777-7777-7777-777777777777"),
            TenantId: Guid.Parse("88888888-8888-8888-8888-888888888888"),
            MessageId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            OccurredAt: DateTimeOffset.Parse("2026-07-08T10:30:00+07:00"));

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<WorkflowInstanceFinalizedIntegrationEvent>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Should().BeEquivalentTo(evt);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void WorkflowInstanceFinalized_JsonShape_ContainsExpectedProperties()
    {
        var evt = new WorkflowInstanceFinalizedIntegrationEvent(
            Guid.NewGuid(), "FormSubmission", Guid.NewGuid(), "Rejected", Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        var root = JsonDocument.Parse(JsonSerializer.Serialize(evt)).RootElement;

        root.TryGetProperty("InstancePublicId", out _).Should().BeTrue();
        root.TryGetProperty("EntityType", out _).Should().BeTrue();
        root.TryGetProperty("EntityId", out _).Should().BeTrue();
        root.TryGetProperty("FinalStateName", out _).Should().BeTrue();
        root.TryGetProperty("TransitionedBy", out _).Should().BeTrue();
        root.TryGetProperty("TenantId", out _).Should().BeTrue();
        root.TryGetProperty("MessageId", out _).Should().BeTrue();
        root.TryGetProperty("OccurredAt", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void WorkflowInstanceFinalized_ExposesPayloadTenant_ViaMarker()
    {
        var tenantId = Guid.NewGuid();
        var evt = new WorkflowInstanceFinalizedIntegrationEvent(
            Guid.NewGuid(), "FormSubmission", Guid.NewGuid(), "Approved", Guid.NewGuid(),
            tenantId, Guid.NewGuid(), DateTimeOffset.UtcNow);

        ((ITenantScopedIntegrationEvent)evt).TenantId.Should().Be(tenantId);
    }
}
