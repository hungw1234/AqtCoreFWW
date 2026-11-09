using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.Events;
using AqtCoreFW.Notifications.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Notifications.Domain.Tests.Entities;

/// <summary>
/// Behavior tests for the <see cref="Notification"/> aggregate — the status state machine
/// (Pending → Sent/Failed/Read) and the creation event contract.
///
/// Teeth, not getters: each transition asserts the observable state change, and
/// MarkAsRead_CalledTwice pins the idempotency guard (`if (IsRead) return`) — removing
/// that guard re-stamps ReadAt and re-runs the update, which this test rejects.
/// </summary>
public sealed class NotificationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RecipientId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid PublicId = Guid.NewGuid();
    private static readonly Guid MessageId = Guid.NewGuid();

    private static (Notification notification, IGuidGenerator gen) CreateSut()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(PublicId);
        gen.NewIdRandom().Returns(MessageId);

        var notification = Notification.Create(
            gen, TenantId, RecipientId,
            subject: "Hồ sơ cần xử lý",
            body: "Bạn có một hồ sơ mới.",
            channel: NotificationChannel.Email,
            createdBy: ActorId,
            correlationId: "case-42");

        return (notification, gen);
    }

    [Fact]
    public void Create_StartsPending_AndRaisesCreatedEventCarryingPublicId()
    {
        var (notification, _) = CreateSut();

        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.IsRead.Should().BeFalse();
        notification.PublicId.Should().Be(PublicId, "PublicId is minted from the monotonic generator");
        notification.CorrelationId.Should().Be("case-42");

        var evt = notification.DomainEvents.OfType<NotificationCreatedEvent>().Should().ContainSingle().Subject;
        evt.NotificationPublicId.Should().Be(PublicId, "the event carries the T2 AK, not the internal Id");
        evt.Channel.Should().Be("email");
        evt.MessageId.Should().Be(MessageId, "MessageId comes from the random generator (Rule1)");
    }

    [Fact]
    public void MarkAsSent_TransitionsToSent_AndStampsSentAt()
    {
        var (notification, _) = CreateSut();

        notification.MarkAsSent();

        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.SentAt.Should().NotBeNull();
        notification.IsRead.Should().BeFalse();
    }

    [Fact]
    public void MarkAsFailed_TransitionsToFailed()
    {
        var (notification, _) = CreateSut();

        notification.MarkAsFailed();

        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.SentAt.Should().BeNull("a failed send never stamps SentAt");
    }

    [Fact]
    public void MarkAsRead_SetsReadStateStatusAndTimestamp()
    {
        var (notification, _) = CreateSut();

        notification.MarkAsRead();

        notification.IsRead.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatus.Read);
        notification.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkAsRead_CalledTwice_IsIdempotent()
    {
        var (notification, _) = CreateSut();

        notification.MarkAsRead();
        var firstReadAt = notification.ReadAt;

        notification.MarkAsRead(); // guard: second call must be a no-op

        notification.IsRead.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatus.Read);
        notification.ReadAt.Should().Be(firstReadAt, "the idempotency guard must not re-stamp ReadAt");
    }
}
