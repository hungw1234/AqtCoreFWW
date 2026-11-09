using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Notifications.Domain.Events;

/// <summary>
/// Internal event — dispatched via MediatR within same transaction.
/// Triggers notification delivery side-effects within the module boundary.
/// NotificationPublicId: Guid (T2 AK — PublicId from Notification aggregate).
/// TenantId/RecipientUserId: Guid (FK exception list — tenant/identity refs).
/// Phase-02c: MessageId moved to positional ctor param (Rule1 — no Guid.NewGuid() defaults).
/// </summary>
public sealed record NotificationCreatedEvent(
    Guid NotificationPublicId,
    Guid TenantId,
    Guid RecipientUserId,
    string Channel,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
