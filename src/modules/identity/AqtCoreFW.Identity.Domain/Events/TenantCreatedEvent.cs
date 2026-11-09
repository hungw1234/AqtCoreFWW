using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Events;

/// <summary>Raised after a new Tenant is created — consumed by Audit module.</summary>
public sealed record TenantCreatedEvent(
    Guid TenantId,
    string Name,
    string Code,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
