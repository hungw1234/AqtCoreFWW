using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Collaboration.Domain.Events;

/// <summary>
/// Raised when a message is sent in a conversation.
/// Payload uses T2 PublicId AK (MessagePublicId) for the message AK.
/// ConversationInternalId: long (T2 internal PK — fast DB lookup for subscribers).
/// SenderId/TenantId: Guid (identity/tenant FK exception list).
/// Phase-02c: MessageId moved to positional ctor param (Rule1 — no Guid.NewGuid() defaults).
/// </summary>
public sealed record MessageSentEvent(
    Guid MessagePublicId,
    long ConversationInternalId,
    Guid SenderId,
    Guid TenantId,
    Guid MessageId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
