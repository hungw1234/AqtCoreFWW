using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised after new user is persisted — consumed by Audit module (Phase 05).</summary>
public sealed record UserCreatedEvent(
    Guid UserId,
    string FullName,
    string Email,
    Guid? TenantId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
