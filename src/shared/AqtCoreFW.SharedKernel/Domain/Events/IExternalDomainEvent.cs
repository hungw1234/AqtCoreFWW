namespace AqtCoreFW.SharedKernel.Domain.Events;

/// <summary>
/// External domain event — persisted to Outbox, published via MassTransit after commit.
/// Use for: cross-module integration (e.g. CaseCreated → Audit, Notifications).
/// IMPORTANT: Must contain IDs only — NO PII (ArchUnit enforced in Phase 10).
/// </summary>
public interface IExternalDomainEvent : IDomainEvent { }
