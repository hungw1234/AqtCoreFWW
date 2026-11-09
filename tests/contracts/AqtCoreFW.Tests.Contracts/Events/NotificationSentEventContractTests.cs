using System.Text.Json;
using AqtCoreFW.Notifications.Domain.Events;
using FluentAssertions;

namespace AqtCoreFW.Tests.Contracts.Events;

/// <summary>
/// Contract test: NotificationSentEvent serialization round-trip.
/// Phase-4b: NotificationId → NotificationPublicId (Guid T2 AK).
/// EventId → MessageId (IDomainEvent contract rename phase-04a/B1).
/// Phase-02c: MessageId is now positional ctor param (Rule1 compliant — no Guid.NewGuid() default).
/// Ensures JSON shape is stable — any breaking change fails this test.
/// </summary>
public sealed class NotificationSentEventContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void NotificationSentEvent_Serialization_RoundTrip_IsStable()
    {
        var evt = new NotificationSentEvent(
            NotificationPublicId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            TenantId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            RecipientUserId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Channel: "Email",
            MessageId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<NotificationSentEvent>(json);

        deserialized.Should().NotBeNull();
        deserialized!.NotificationPublicId.Should().Be(evt.NotificationPublicId);
        deserialized.TenantId.Should().Be(evt.TenantId);
        deserialized.RecipientUserId.Should().Be(evt.RecipientUserId);
        deserialized.Channel.Should().Be(evt.Channel);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void NotificationSentEvent_JsonShape_ContainsExpectedProperties()
    {
        var evt = new NotificationSentEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Sms", Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("NotificationPublicId", out _).Should().BeTrue();
        root.TryGetProperty("TenantId", out _).Should().BeTrue();
        root.TryGetProperty("RecipientUserId", out _).Should().BeTrue();
        root.TryGetProperty("Channel", out _).Should().BeTrue();
        root.TryGetProperty("MessageId", out _).Should().BeTrue();
        root.TryGetProperty("OccurredAt", out _).Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "Contract")]
    [InlineData("Email")]
    [InlineData("Sms")]
    [InlineData("InApp")]
    public void NotificationSentEvent_Channel_SurvivesRoundTrip(string channel)
    {
        var evt = new NotificationSentEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), channel, Guid.NewGuid());

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<NotificationSentEvent>(json);

        deserialized!.Channel.Should().Be(channel);
    }
}
