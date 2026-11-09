using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.SharedKernel.Contracts.Events;

/// <summary>
/// Integration event raised on the <c>WorkflowInstance</c> aggregate (Workflow module) when an
/// instance transitions into a FINAL state for a FormSubmission entity. Replaces the former
/// in-process <c>WorkflowTransitionedFormSubmissionHandler</c> MediatR notification handler.
/// Wire protocol: IDs + state-name discriminator + metadata only. NO submission payload (PII).
/// Consumers must re-hydrate full submission context from their own data sources if needed.
/// Idempotency key: <see cref="InstancePublicId"/> + <see cref="MessageId"/> — consumers MUST
/// de-duplicate on this composite pair (a single instance can only finalize once, but the pair
/// keeps the key stable in shape with every other <see cref="IExternalDomainEvent"/> implementer).
/// Delivery semantics: at-least-once — <see cref="IExternalDomainEvent"/> routes this through the
/// Outbox: <c>WorkflowInstance</c> IS an <c>IAggregateRoot</c>, so <c>OutboxInterceptor</c> captures
/// it straight off <c>DomainEvents</c> at the transition's <c>SaveChanges</c> — atomic with the
/// state-transition write into <c>workflow.OutboxMessages</c> (no separate <c>IOutboxWriter</c>
/// call needed, unlike FormSubmission/AuditLogEntry which are not aggregate roots). A rollback of
/// the transition also drops this event; a broker outage after commit no longer loses it (outbox
/// retry/dispatch handles delivery). <see cref="MessageId"/> is the per-producer monotonic (V7) id
/// — same field every other <see cref="IExternalDomainEvent"/> implementer carries.
/// </summary>
public sealed record WorkflowInstanceFinalizedIntegrationEvent(
    Guid InstancePublicId,
    string EntityType,       // discriminator, e.g. "FormSubmission"
    Guid EntityId,           // the linked entity's PublicId (FormSubmission.PublicId)
    string FinalStateName,   // workflow final-state name; consumer maps to approve/reject
    Guid TransitionedBy,
    Guid TenantId,
    Guid MessageId,
    DateTimeOffset OccurredAt) : IExternalDomainEvent, ITenantScopedIntegrationEvent
{
    // Slice: expose the (always-present) instance tenant to TenantScopeConsumeFilter, which pins
    // the consume scope so the Forms write sets SESSION_CONTEXT / honours RLS.
    // Explicit impl: the marker is Guid? but this event's tenant is always known (non-nullable).
    Guid? ITenantScopedIntegrationEvent.TenantId => TenantId;
}
