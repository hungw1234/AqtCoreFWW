using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Collaboration.Domain.Events;

/// <summary>
/// Raised when a new conversation is created.
/// Payload uses T2 PublicId AK (ConversationPublicId) — never internal long Id.
/// CreatedBy/TenantId: Guid (identity/tenant FK exception list).
/// Phase-02c: MessageId moved to positional ctor param (Rule1 — no Guid.NewGuid() defaults).
/// </summary>
public sealed record ConversationCreatedEvent(
    Guid ConversationPublicId,
    Guid CreatedBy,
    Guid TenantId,
    Guid MessageId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
