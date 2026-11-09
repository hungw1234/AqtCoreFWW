using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Notifications.Infrastructure.Realtime;

/// <summary>
/// SignalR hub for real-time in-app notifications.
/// Group targeting MANDATORY (perf spec S260307): user:{userId} + org:{orgId}.
/// NEVER uses Clients.All — prevents Redis backplane fan-out to all connections.
/// Requires authenticated JWT connection.
/// </summary>
[Authorize]
public sealed class NotificationsHub(ILogger<NotificationsHub> logger) : Hub
{
    // Claim types matching OpenIddict token
    private const string UserIdClaim = "sub";
    private const string OrgIdClaim = "org_id";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(UserIdClaim)?.Value;
        var orgId = Context.User?.FindFirst(OrgIdClaim)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            logger.LogDebug("SignalR: user {UserId} joined user group", userId);
        }

        if (!string.IsNullOrEmpty(orgId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"org:{orgId}");
            logger.LogDebug("SignalR: user {UserId} joined org group {OrgId}", userId, orgId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(UserIdClaim)?.Value;
        var orgId = Context.User?.FindFirst(OrgIdClaim)?.Value;

        if (!string.IsNullOrEmpty(userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{userId}");

        if (!string.IsNullOrEmpty(orgId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"org:{orgId}");

        await base.OnDisconnectedAsync(exception);
    }
}
