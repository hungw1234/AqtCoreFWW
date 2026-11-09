using AqtCoreFW.Files.Domain.Entities;

namespace AqtCoreFW.Files.Application.DTOs;

/// <summary>
/// File metadata response DTO — safe to return to clients (no storage internals).
/// Phase-4b B3b: Id = PublicId (Guid AK), not the internal long PK.
/// </summary>
public sealed record FileMetadataDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string ChecksumSha256,
    FileStatus Status,
    Guid TenantId,
    Guid UploadedBy,
    Guid? CaseId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
