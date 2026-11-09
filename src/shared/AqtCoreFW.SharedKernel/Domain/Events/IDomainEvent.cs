using MediatR;

namespace AqtCoreFW.SharedKernel.Domain.Events;

/// <summary>
/// Base marker for all domain events. Implements MediatR INotification for in-process dispatch.
///
/// Phase-2 Foundation Refactor (2026-05-03) — payload contract:
///   * For T2 (API-exposed) entity events, payloads MUST reference the entity's Guid PublicId, NOT long Id.
///   * For T3 (idempotency / event / append-log) entity events, payloads MUST reference Guid MessageId.
///   * NEVER embed raw long Id in event payloads — Id is 0 pre-SaveChanges (DB-assigned IDENTITY).
///
/// Phase-4a B1 (2026-05-03): EventId renamed to MessageId aligning with IMessageIdentified marker.
/// Per-module event implementers renamed in their owning batches (B2-B5b). Consumer-side handlers fixed in Phase-4b.
/// </summary>
public interface IDomainEvent : INotification
{
    Guid MessageId { get; }
    DateTimeOffset OccurredAt { get; }
}
