using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Notifications.Domain.Events;

/// <summary>
/// External event — dispatched via Outbox → MassTransit after notification is sent.
/// IDs only — no PII per outbox security spec.
/// NotificationPublicId: Guid (T2 AK — PublicId from Notification aggregate).
/// TenantId/RecipientUserId: Guid (FK exception list — tenant/identity refs).
/// Phase-02c: MessageId moved to positional ctor param (Rule1 — no Guid.NewGuid() defaults).
/// </summary>
public sealed record NotificationSentEvent(
    Guid NotificationPublicId,
    Guid TenantId,
    Guid RecipientUserId,
    string Channel,
    Guid MessageId) : IExternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
