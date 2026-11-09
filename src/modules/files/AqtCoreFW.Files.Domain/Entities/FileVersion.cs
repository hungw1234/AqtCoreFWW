using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Files.Domain.Entities;

/// <summary>
/// Represents a specific version of a FileRecord.
/// VersionNumber increments monotonically per FileRecord.
/// ContentHash (SHA-256) enables deduplication and integrity verification.
/// StoragePath is the physical path in object storage for this version.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
///
/// FK flips: FileRecordId Guid → long (FileRecord.Id is now bigint).
/// UploadedBy stays Guid (FK exception list: references AspNetUsers.Id).
/// TenantId stays Guid (FK exception list).
/// </summary>
public sealed class FileVersion : IMessageIdentified
{
    private FileVersion() { } // EF Core

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>FK to FileRecord.Id (bigint identity PK).</summary>
    public long FileRecordId { get; private set; }

    public int VersionNumber { get; private set; }
    public string StoragePath { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;

    /// <summary>UploadedBy stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid UploadedBy { get; private set; }

    public DateTime UploadedAt { get; private set; }
    public string? Comment { get; private set; }

    /// <summary>TenantId stays Guid (FK exception list).</summary>
    public Guid TenantId { get; private set; }

    public static FileVersion Create(
        IGuidGenerator gen,
        long fileRecordId,
        int versionNumber,
        string storagePath,
        long fileSize,
        string contentHash,
        Guid uploadedBy,
        Guid tenantId,
        string? comment = null)
    {
        return new FileVersion
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            FileRecordId = fileRecordId,
            VersionNumber = versionNumber,
            StoragePath = storagePath,
            FileSize = fileSize,
            ContentHash = contentHash,
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow,
            Comment = comment,
            TenantId = tenantId
        };
    }
}
