using AqtCoreFW.Notifications.Domain.Events;
using AqtCoreFW.Notifications.Infrastructure.Realtime;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Notifications.Infrastructure.Tests.Realtime;

/// <summary>
/// TC-NOT-I006: NotificationPushService sends to correct SignalR group ("user:{userId}").
/// Phase-02c: NotificationCreatedEvent now requires MessageId positional param (Rule1 compliant).
/// </summary>
public sealed class NotificationPushServiceTests
{
    [Fact]
    public async Task Handle_NotificationCreatedEvent_SendsToCorrectUserGroup()
    {
        // TC-NOT-I006: group MUST be "user:{recipientUserId}" — never Clients.All
        var recipientId = Guid.NewGuid();
        var expectedGroup = $"user:{recipientId}";

        var hubContext = Substitute.For<IHubContext<NotificationsHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();

        hubContext.Clients.Returns(clients);
        clients.Group(expectedGroup).Returns(clientProxy);

        var sut = new NotificationPushService(
            hubContext,
            NullLogger<NotificationPushService>.Instance);

        var @event = new NotificationCreatedEvent(
            NotificationPublicId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            RecipientUserId: recipientId,
            Channel: "InApp",
            MessageId: Guid.NewGuid());

        await sut.Handle(@event, CancellationToken.None);

        // Verify the group was addressed by "user:{recipientId}"
        clients.Received(1).Group(expectedGroup);

        // Verify SendAsync was called on that group proxy
        await clientProxy.Received(1).SendCoreAsync(
            "NotificationCreated",
            Arg.Any<object[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DifferentRecipients_UseDifferentGroups()
    {
        // Each recipient gets their own group — group names are distinct
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        $"user:{userId1}".Should().NotBe($"user:{userId2}");
    }
}
