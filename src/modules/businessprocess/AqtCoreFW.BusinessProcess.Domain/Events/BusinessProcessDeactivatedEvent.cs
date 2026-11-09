using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.BusinessProcess.Domain.Events;

/// <summary>
/// External event — raised when a BusinessProcessDefinition is deactivated (IsActive: true → false).
/// Dispatched via Outbox → MassTransit after the SaveChanges that flipped IsActive.
/// IDs only — no PII per outbox security spec (ArchUnit enforced).
/// Downstream consumers may pause new BP-Run intake while in-flight WorkflowInstances continue.
/// </summary>
public sealed record BusinessProcessDeactivatedEvent(
    Guid BusinessProcessPublicId,
    Guid TenantId,
    Guid DeactivatedBy,
    Guid MessageId) : IExternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
