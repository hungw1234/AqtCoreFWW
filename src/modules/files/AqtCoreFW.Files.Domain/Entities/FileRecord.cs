using AqtCoreFW.Files.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Files.Domain.Entities;

/// <summary>
/// FileRecord aggregate root — represents a stored file with virus scan lifecycle.
/// Two-phase upload: Quarantined (uploaded) → Available (scan pass) | Rejected (scan fail).
/// Storage key is UUID-based (never user-supplied filename) — prevents path traversal.
///
/// T2: long PK (Id, DB-assigned) + Guid AK (PublicId, set at create via IGuidGenerator v7).
/// UploadedBy stays Guid (FK exception list: references AspNetUsers.Id).
/// TenantId stays Guid (FK exception list).
/// CaseId stays Guid (orphaned correlation ID; Cases module removed).
/// Events pass PublicId (not long Id) per IDomainEvent payload contract.
/// Phase-02c: MarkAvailable/MarkRejected require IGuidGenerator gen for Rule1-compliant event MessageId.
/// </summary>
public sealed class FileRecord : AuditableEntity, IPublicIdentified, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Public-facing alternate key (Guid v7 monotonic). Used in API URLs and DTOs.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>TenantId — stays Guid (FK exception list).</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Original user-supplied filename — stored in metadata only, never used as storage key.</summary>
    public string OriginalFileName { get; private set; } = string.Empty;

    /// <summary>UUID-based storage key: {tenantId}/{uuid}.{safeExt} — prevents path traversal.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string ChecksumSha256 { get; private set; } = string.Empty;
    public FileStatus Status { get; private set; }

    [DataClassification(DataClassificationLevel.Internal)]
    /// <summary>UploadedBy stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid UploadedBy { get; private set; }

    /// <summary>CaseId stays Guid — orphaned correlation ID (Cases module removed 2026-05-01).</summary>
    public Guid? CaseId { get; private set; }

    /// <summary>Uploader full name — PII, encrypted at rest via EncryptedStringConverter.</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string? UploaderDisplayName { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private FileRecord() { } // EF Core

    public static FileRecord Create(
        IGuidGenerator gen,
        Guid tenantId,
        string originalFileName,
        string storageKey,
        string contentType,
        long sizeBytes,
        string checksumSha256,
        Guid uploadedBy,
        Guid? caseId = null,
        string? uploaderDisplayName = null,
        ClassificationLevel classification = ClassificationLevel.Internal)
    {
        var record = new FileRecord
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            OriginalFileName = originalFileName,
            StorageKey = storageKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            ChecksumSha256 = checksumSha256,
            Status = FileStatus.Quarantined,
            UploadedBy = uploadedBy,
            CaseId = caseId,
            UploaderDisplayName = uploaderDisplayName,
            ClassificationLevel = classification
        };
        record.SetAuditCreate(uploadedBy);
        return record;
    }

    /// <summary>Called by ClamAvScanJob after clean scan — file becomes accessible. Passes PublicId to events.</summary>
    public void MarkAvailable(IGuidGenerator gen)
    {
        Status = FileStatus.Available;
        MarkUpdated();
        AddDomainEvent(new FileUploadedEvent(PublicId, TenantId, OriginalFileName, SizeBytes, gen.NewIdRandom()));
    }

    /// <summary>Called by ClamAvScanJob when virus detected — file must be deleted from storage. Passes PublicId to events.</summary>
    public void MarkRejected(IGuidGenerator gen)
    {
        Status = FileStatus.Rejected;
        MarkUpdated();
        AddDomainEvent(new FileQuarantinedEvent(PublicId, TenantId, OriginalFileName, gen.NewIdRandom()));
    }

    /// <summary>Soft delete — removes logical access but preserves audit trail.</summary>
    public new void Delete()
    {
        MarkDeleted();
        MarkUpdated();
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Two-phase virus scan lifecycle for file records.</summary>
public enum FileStatus
{
    /// <summary>Uploaded to MinIO, pending ClamAV scan. NOT accessible for download.</summary>
    Quarantined = 0,

    /// <summary>ClamAV scan passed — file is accessible for download.</summary>
    Available = 1,

    /// <summary>ClamAV detected virus — file deleted from MinIO, metadata retained for audit.</summary>
    Rejected = 2
}
