using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised when tenant status changes (e.g. Active → Suspended or → Archived).</summary>
public sealed record TenantStatusChangedEvent(
    Guid TenantId,
    TenantStatus OldStatus,
    TenantStatus NewStatus,
    Guid ActorId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
