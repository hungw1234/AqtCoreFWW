using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Files.Domain.Events;

/// <summary>
/// Raised after ClamAV scan passes — file status becomes Available.
/// FilePublicId: Guid PublicId of FileRecord (T2 AK) — not the long internal Id.
/// B3 rename: EventId → MessageId (IDomainEvent contract, phase-04a/B1 interface change).
/// B3 change: FileId (VO struct) → Guid FilePublicId (T2 PublicId AK).
/// Phase-02c: MessageId moved to positional ctor param (Rule1 — no Guid.NewGuid() defaults).
/// </summary>
public sealed record FileUploadedEvent(
    Guid FilePublicId,
    Guid TenantId,
    string OriginalFileName,
    long SizeBytes,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
