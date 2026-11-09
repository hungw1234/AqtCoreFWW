using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.SharedKernel.Contracts.Events;

/// <summary>
/// Integration event enqueued by Forms module AFTER a form submission commits.
/// Consumers (e.g., Notifications) subscribe via MassTransit <c>IConsumer&lt;T&gt;</c> — no
/// cross-module project reference required, message type lives in Contracts.
/// Wire protocol: IDs + template metadata only. NO submission payload (PII). Consumers must
/// re-hydrate full submission context from their own data sources if needed.
/// Idempotency key: <see cref="SubmissionId"/> — consumers MUST de-duplicate on this id.
/// Delivery semantics: at-least-once — <see cref="IExternalDomainEvent"/> routes this through
/// the Outbox (Slice 1B-1): Forms enqueues it via <c>IOutboxWriter</c> inside the SAME local SQL
/// transaction as the submission write (FormSubmission is not yet an <c>IAggregateRoot</c>, so it
/// cannot be captured by <c>OutboxInterceptor</c> — the writer is the module-agnostic escape
/// hatch for that case), so a rollback of the submission also drops this event; a broker outage
/// after commit no longer loses it (outbox retry/dispatch handles delivery). <see cref="MessageId"/>
/// is the per-producer monotonic (V7) id — same field every other <see cref="IExternalDomainEvent"/>
/// implementer carries.
///
/// <para><b>M1 fix:</b> <see cref="OccurredAt"/> is a constructor parameter (mirrors
/// <c>AuditLogAppendedIntegrationEvent</c>), NOT a get-only body-property initializer. A
/// body-property initializer re-runs at EVERY construction — including the one STJ performs on
/// outbox redelivery/dispatch, since a get-only property with no public setter can't be
/// round-tripped by the deserializer — so it silently produced a fresh <c>DateTimeOffset.UtcNow</c>
/// per dispatch instead of the original enqueue time. Callers MUST pass the real enqueue-time
/// timestamp explicitly.</para>
/// </summary>
public sealed record FormSubmissionAcknowledgedIntegrationEvent(
    Guid SubmissionId,
    Guid FormTemplateId,
    Guid TenantId,
    Guid SubmittedBy,
    Guid MessageId,
    DateTimeOffset OccurredAt) : IExternalDomainEvent, ITenantScopedIntegrationEvent
{
    // Slice 1D: expose the (always-present) submission tenant to TenantScopeConsumeFilter, which
    // pins the consume scope so the Notifications write sets SESSION_CONTEXT / honours RLS.
    // Explicit impl: the marker is Guid? but this event's tenant is always known (non-nullable).
    Guid? ITenantScopedIntegrationEvent.TenantId => TenantId;
}
