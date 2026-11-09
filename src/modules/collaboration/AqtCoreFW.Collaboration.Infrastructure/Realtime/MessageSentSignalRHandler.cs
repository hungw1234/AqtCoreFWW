using AqtCoreFW.Collaboration.Application.DTOs;
using AqtCoreFW.Collaboration.Application.Telemetry;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Collaboration.Infrastructure.Realtime;

/// <summary>
/// Broadcasts new messages to SignalR conversation groups in real-time.
/// Triggered by MessageSentEvent domain event after message persistence.
/// </summary>
public sealed class MessageSentSignalRHandler(
    IReadDbConnection db,
    IIdentityModuleClient identityClient,
    IHubContext<ChatHub> hubContext,
    ILogger<MessageSentSignalRHandler> logger)
    : INotificationHandler<MessageSentEvent>
{
    public async Task Handle(MessageSentEvent notification, CancellationToken ct)
    {
        // Realtime delivery is best-effort. This handler runs synchronously on the publish of
        // MessageSentEvent, which fires AFTER the message is already persisted. A fault in the
        // read-back query or the SignalR broadcast must NOT bubble up — it would turn a committed
        // write into a 500 while the row is safely saved. Log-and-swallow; clients reconcile the
        // missed message on their next fetch or reconnect.
        try
        {
            await BroadcastAsync(notification, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Let cooperative cancellation propagate (request aborted) — it is not a broadcast fault.
            // Count the swallowed fault so a sustained spike (systemic) is distinguishable from a
            // transient miss on the dashboard — per-message warnings alone give no aggregate signal.
            CollaborationMetrics.BroadcastFailures.Add(
                1, new KeyValuePair<string, object?>("tenant", notification.TenantId.ToString()));
            logger.LogWarning(ex,
                "Realtime broadcast failed for message {MessagePublicId} in tenant {TenantId}; message persisted, delivery skipped.",
                notification.MessagePublicId, notification.TenantId);
        }
    }

    private async Task BroadcastAsync(MessageSentEvent notification, CancellationToken ct)
    {
        // Query persisted message — project PublicIds to FE-facing Guids; resolve reply target's PublicId via self-join.
        const string sql = """
            SELECT m.PublicId AS Id, c.PublicId AS ConversationId, m.SenderId,
                   m.Content, m.ContentType, r.PublicId AS ReplyToId,
                   m.CreatedAt, m.EditedAt, m.IsSystemMessage
            FROM collab.Messages m
            INNER JOIN collab.Conversations c ON c.Id = m.ConversationId
            LEFT JOIN collab.Messages r ON r.Id = m.ReplyToId
            WHERE m.PublicId = @MessagePublicId AND m.IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<MessageRow>(
            sql, new { notification.MessagePublicId }, ct);
        if (row is null) return;

        // Resolve sender name
        var users = await identityClient.GetUserInfoByIdsAsync(
            [notification.SenderId], notification.TenantId, ct);
        var senderName = users.TryGetValue(notification.SenderId, out var u) ? u.FullName : null;

        var dto = new MessageDto(
            row.Id, row.ConversationId, row.SenderId, senderName,
            row.Content, row.ContentType, row.ReplyToId,
            row.CreatedAt, row.EditedAt, false, row.IsSystemMessage);

        // Broadcast to all clients in the conversation group — group key uses Conversation PublicId (matches ChatHub.JoinConversation).
        await hubContext.Clients
            .Group($"conversation:{row.ConversationId}")
            .SendAsync("ReceiveMessage", dto, ct);
    }

    internal sealed class MessageRow
    {
        public Guid Id { get; init; }
        public Guid ConversationId { get; init; }
        public Guid SenderId { get; init; }
        public string Content { get; init; } = "";
        public string ContentType { get; init; } = "Text";
        public Guid? ReplyToId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTime? EditedAt { get; init; }
        public bool IsSystemMessage { get; init; }
    }
}
