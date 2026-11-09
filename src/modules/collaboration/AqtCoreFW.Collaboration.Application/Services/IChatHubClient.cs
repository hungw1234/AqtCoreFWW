using AqtCoreFW.Collaboration.Application.DTOs;

namespace AqtCoreFW.Collaboration.Application.Services;

/// <summary>
/// SignalR strongly-typed hub client interface.
/// Used by IHubContext{ChatHub, IChatHubClient} to push events to connected clients.
/// </summary>
public interface IChatHubClient
{
    Task ReceiveMessage(MessageDto message);
    Task ReceiveReadReceipt(Guid messageId, Guid userId, string? fullName, DateTime readAt);
    Task UserJoined(Guid userId, Guid conversationId);
    Task UserLeft(Guid userId, Guid conversationId);
    Task PresenceChanged(Guid userId, string status);
}
