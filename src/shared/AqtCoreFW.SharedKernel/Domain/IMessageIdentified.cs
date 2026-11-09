namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>
/// Marker for T3 (idempotency / event / append-log) entities that carry a public-facing Guid message id.
/// Phase-2 Foundation Refactor (2026-05-03): T3 entities use MessageId for outbox dedup and event correlation.
/// MessageId set by domain factory via IGuidGenerator.NewIdMonotonic() (v7 — time-ordering benefit for FIFO dispatch).
/// </summary>
public interface IMessageIdentified
{
    Guid MessageId { get; }
}
