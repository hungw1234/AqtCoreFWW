using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.BusinessProcess.Domain.Events;

/// <summary>
/// External event — raised when a BusinessProcessDefinition is activated (IsActive: false → true).
/// Dispatched via Outbox → MassTransit after the SaveChanges that flipped IsActive.
/// IDs only — no PII per outbox security spec (ArchUnit enforced).
/// MessageId from caller (V7 monotonic) — Rule 1: no Guid.NewGuid() defaults inside events.
/// </summary>
public sealed record BusinessProcessActivatedEvent(
    Guid BusinessProcessPublicId,
    Guid TenantId,
    Guid ActivatedBy,
    Guid MessageId) : IExternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
