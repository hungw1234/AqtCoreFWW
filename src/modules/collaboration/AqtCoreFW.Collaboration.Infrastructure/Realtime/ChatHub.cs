using AqtCoreFW.Collaboration.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Collaboration.Infrastructure.Realtime;

/// <summary>
/// SignalR hub for real-time chat — manages conversation group membership.
/// Actual message delivery is done via IHubContext{ChatHub, IChatHubClient} from command handlers.
/// Requires authenticated JWT connection.
/// </summary>
[Authorize]
public sealed class ChatHub(IConversationRepository conversationRepository, ILogger<ChatHub> logger) : Hub
{
    private const string UserIdClaim = "sub";
    private const string TenantIdClaim = "tenant_id";

    /// <summary>
    /// Adds the connection to the conversation group for targeted delivery — ONLY if the caller is a
    /// participant. Without this gate any authenticated user could join any conversation group by
    /// guessing its Guid and receive every real-time broadcast for it (IDOR on the receive path).
    /// </summary>
    public async Task JoinConversation(Guid conversationId)
    {
        var userId = GetClaimGuid(UserIdClaim);
        var tenantId = GetClaimGuid(TenantIdClaim);

        if (userId is null || tenantId is null ||
            !await conversationRepository.IsMemberAsync(conversationId, tenantId.Value, userId.Value))
        {
            logger.LogWarning(
                "SignalR: denied join to conversation:{ConversationId} for user {UserId} — not a participant.",
                conversationId, userId);
            throw new HubException("You are not a participant of this conversation.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
        logger.LogDebug("SignalR: user {UserId} joined conversation:{ConversationId}", userId, conversationId);
    }

    /// <summary>Removes the connection from the conversation group.</summary>
    public async Task LeaveConversation(Guid conversationId)
    {
        var userId = Context.User?.FindFirst(UserIdClaim)?.Value;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
        logger.LogDebug("SignalR: user {UserId} left conversation:{ConversationId}", userId, conversationId);
    }

    // Hub invocations have no HttpContext (ITenantContext can't resolve here), so claims are read
    // straight off the connection's authenticated principal.
    private Guid? GetClaimGuid(string claimType) =>
        Guid.TryParse(Context.User?.FindFirst(claimType)?.Value, out var value) ? value : null;

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(UserIdClaim)?.Value;
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(UserIdClaim)?.Value;
        if (!string.IsNullOrEmpty(userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{userId}");

        await base.OnDisconnectedAsync(exception);
    }
}
