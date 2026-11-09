using AqtCoreFW.Files.Domain.Entities;

namespace AqtCoreFW.Files.Application.DTOs;

/// <summary>
/// DTO for FileVersion read operations.
/// Phase-4b B3b: T3 entity — InternalId (long) is internal; MessageId (Guid) is the public AK.
/// FileRecordId flipped Guid → long (FK to FileRecord.Id bigint).
/// </summary>
public sealed record FileVersionDto(
    Guid MessageId,
    // arch:suppress R_T2FK FileRecord: deliberate T3 cross-aggregate FK pattern (Phase-4b B3b) — internal storage adapter contract
    long FileRecordId,
    int VersionNumber,
    string StoragePath,
    long FileSize,
    string ContentHash,
    Guid UploadedBy,
    DateTime UploadedAt,
    string? Comment,
    Guid TenantId);

/// <summary>
/// DTO for DocumentTemplate read operations.
/// Phase-4b B3b: T1 entity — Id is long (bigint identity PK, DB-assigned).
/// </summary>
public sealed record DocumentTemplateDto(
    long Id,
    string Name,
    string Code,
    string? Description,
    DocumentOutputFormat OutputFormat,
    string TemplateContent,
    DocumentTemplateStatus Status,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    Guid CreatedBy);

/// <summary>
/// DTO for RetentionPolicy read operations.
/// Phase-4b B3b: T1 entity — Id is long (bigint identity PK, DB-assigned).
/// </summary>
public sealed record RetentionPolicyDto(
    long Id,
    string Name,
    string DocumentType,
    int RetainDays,
    int? ArchiveAfterDays,
    int? DestroyAfterDays,
    bool IsActive,
    Guid TenantId);

/// <summary>
/// DTO for RecordLifecycle read operations.
/// Phase-4b B3b: T3 entity — InternalId (long) is internal; MessageId (Guid) is the public AK.
/// FileRecordId flipped Guid → long (FK to FileRecord.Id bigint).
/// RetentionPolicyId flipped Guid? → long? (FK to RetentionPolicy.Id bigint).
/// </summary>
public sealed record RecordLifecycleDto(
    Guid MessageId,
    // arch:suppress R_T2FK FileRecord: deliberate T3 cross-aggregate FK pattern (Phase-4b B3b) — internal storage adapter contract
    long FileRecordId,
    RecordLifecycleStatus CurrentStatus,
    long? RetentionPolicyId,
    DateTime? ArchivedAt,
    DateTime? ScheduledDestroyAt,
    DateTime? DestroyedAt,
    Guid? DestroyedBy);
