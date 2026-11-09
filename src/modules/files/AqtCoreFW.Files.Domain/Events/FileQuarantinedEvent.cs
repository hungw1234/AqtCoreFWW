using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Files.Domain.Events;

/// <summary>
/// Raised when ClamAV detects a virus — file status becomes Rejected.
/// FilePublicId: Guid PublicId of FileRecord (T2 AK) — not the long internal Id.
/// B3 rename: EventId → MessageId (IDomainEvent contract, phase-04a/B1 interface change).
/// B3 change: FileId (VO struct) → Guid FilePublicId (T2 PublicId AK).
/// Phase-02c: MessageId moved to positional ctor param (Rule1 — no Guid.NewGuid() defaults).
/// </summary>
public sealed record FileQuarantinedEvent(
    Guid FilePublicId,
    Guid TenantId,
    string OriginalFileName,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
