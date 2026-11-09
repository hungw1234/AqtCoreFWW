using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised when a role is soft-deleted (deactivated) — consumed by Audit module.</summary>
public sealed record RoleDeactivatedEvent(
    Guid RoleId,
    Guid ActorId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
