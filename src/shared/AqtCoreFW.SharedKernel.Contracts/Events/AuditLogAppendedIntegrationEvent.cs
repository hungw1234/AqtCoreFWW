using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.SharedKernel.Contracts.Events;

/// <summary>
/// Integration event published by Audit module AFTER an audit log entry is appended (sync path).
/// Dual-write: existing sync repository.AddAsync remains authoritative — this event is purely
/// preparation for future microservice extraction (consumer = noop today).
/// Wire protocol: server-side IDs + classification metadata only. NO <c>dataSnapshot</c>
/// payload — that field may contain PII per <c>AuditPiiAnonymizer</c> rules and MUST NOT
/// cross the bus boundary in clear text.
/// Idempotency key: <see cref="AuditEntryId"/> — consumers MUST de-duplicate on this id.
/// Delivery semantics: at-least-once (Slice 1B-2) — <see cref="IExternalDomainEvent"/> routes
/// this through the Outbox: <c>AuditService</c> enqueues it via <c>IOutboxWriter</c> inside the
/// SAME local SQL transaction as the audit row append (AuditLogEntry is not an
/// <c>IAggregateRoot</c>, so it cannot be captured by <c>OutboxInterceptor</c> — the writer is
/// the module-agnostic escape hatch for that case), so a rollback of the append also drops this
/// event; a broker outage after commit no longer loses it (outbox retry/dispatch handles
/// delivery). <see cref="MessageId"/> is the per-producer monotonic (V7) id — same field every
/// other <see cref="IExternalDomainEvent"/> implementer carries; distinct from
/// <see cref="AuditEntryId"/>, which identifies the audit row itself.
/// </summary>
public sealed record AuditLogAppendedIntegrationEvent(
    Guid AuditEntryId,
    Guid? TenantId,
    Guid? ActorUserId,
    string Action,
    string ModuleName,
    string ResourceType,
    string? ResourceId,
    Guid MessageId,
    DateTimeOffset OccurredAt) : IExternalDomainEvent, ITenantScopedIntegrationEvent;
