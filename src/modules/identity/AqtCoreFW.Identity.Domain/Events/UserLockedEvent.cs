using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised when admin locks or unlocks a user account.</summary>
public sealed record UserLockedEvent(
    Guid UserId,
    bool IsLocked,
    Guid LockedBy,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
