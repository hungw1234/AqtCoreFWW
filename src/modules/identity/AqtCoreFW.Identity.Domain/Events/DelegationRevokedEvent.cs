using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised when a role delegation is revoked — consumed by Audit module.</summary>
public sealed record DelegationRevokedEvent(
    Guid DelegationId,
    Guid DelegatorId,
    Guid DelegateId,
    Guid ActorId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
