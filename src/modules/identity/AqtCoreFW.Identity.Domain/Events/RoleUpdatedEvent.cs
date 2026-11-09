using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised when a role is updated — consumed by Audit module.</summary>
public sealed record RoleUpdatedEvent(
    Guid RoleId,
    string Name,
    Guid ActorId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
