using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using AqtCoreFW.Notifications.Domain.Events;

namespace AqtCoreFW.Notifications.Infrastructure.Realtime;

/// <summary>
/// Handles NotificationCreatedEvent (internal domain event) and pushes to SignalR.
/// Group targeting MANDATORY: always Clients.Group("user:{userId}"), never Clients.All.
/// </summary>
public sealed class NotificationPushService(
    IHubContext<NotificationsHub> hubContext,
    ILogger<NotificationPushService> logger)
    : INotificationHandler<NotificationCreatedEvent>
{
    public async Task Handle(NotificationCreatedEvent notification, CancellationToken cancellationToken)
    {
        var groupName = $"user:{notification.RecipientUserId}";

        await hubContext.Clients.Group(groupName).SendAsync(
            "NotificationCreated",
            new
            {
                notification.NotificationPublicId,
                notification.TenantId,
                notification.RecipientUserId,
                notification.Channel,
                notification.OccurredAt
            },
            cancellationToken);

        logger.LogDebug(
            "SignalR push to group {Group} for notification {NotificationId}",
            groupName, notification.NotificationPublicId);
    }
}
