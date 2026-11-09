using AqtCoreFW.Collaboration.Application.Telemetry;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Collaboration.Infrastructure.Realtime;

/// <summary>
/// Broadcasts a "seen by" read-receipt to the conversation group in real-time.
/// Triggered by MessageReadEvent, which MarkAsReadCommandHandler raises only on a FIRST read (so the
/// event is not emitted on idempotent re-marks).
/// </summary>
public sealed class MessageReadSignalRHandler(
    IIdentityModuleClient identityClient,
    IHubContext<ChatHub> hubContext,
    ILogger<MessageReadSignalRHandler> logger)
    : INotificationHandler<MessageReadEvent>
{
    public async Task Handle(MessageReadEvent notification, CancellationToken ct)
    {
        // Best-effort, same contract as the message broadcast: this runs after the receipt is committed,
        // so a name-resolve or SignalR fault must NOT turn a committed write into a 500. Log-and-swallow;
        // clients reconcile on their next read-receipts fetch.
        try
        {
            var users = await identityClient.GetUserInfoByIdsAsync(
                [notification.ReaderUserId], notification.TenantId, ct);
            var fullName = users.TryGetValue(notification.ReaderUserId, out var u) ? u.FullName : null;

            await hubContext.Clients
                .Group($"conversation:{notification.ConversationPublicId}")
                .SendAsync(
                    "ReceiveReadReceipt",
                    notification.MessagePublicId, notification.ReaderUserId, fullName, notification.ReadAt,
                    ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CollaborationMetrics.BroadcastFailures.Add(
                1, new KeyValuePair<string, object?>("tenant", notification.TenantId.ToString()));
            logger.LogWarning(ex,
                "Realtime read-receipt broadcast failed for message {MessagePublicId} in tenant {TenantId}; receipt persisted, delivery skipped.",
                notification.MessagePublicId, notification.TenantId);
        }
    }
}
