using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised when a new role is created — consumed by Audit module.</summary>
public sealed record RoleCreatedEvent(
    Guid RoleId,
    string Code,
    string Name,
    Guid ActorId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
