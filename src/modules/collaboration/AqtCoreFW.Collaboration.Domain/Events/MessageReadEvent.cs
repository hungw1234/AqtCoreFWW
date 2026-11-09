using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Collaboration.Domain.Events;

/// <summary>
/// Raised when a user reads a message for the FIRST time (a new read-receipt was persisted).
/// Drives the realtime "seen by" push; re-marking an already-read message does NOT raise it.
/// Message/Conversation ids are T2 PublicId AK (Guid) so the SignalR payload is FE-facing.
/// MessageId is the IDomainEvent correlation id (Rule1 — caller-supplied via IGuidGenerator, never defaulted).
/// </summary>
public sealed record MessageReadEvent(
    Guid MessagePublicId,
    Guid ConversationPublicId,
    Guid ReaderUserId,
    Guid TenantId,
    DateTime ReadAt,
    Guid MessageId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
