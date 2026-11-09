using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.UploadFileVersion;

/// <summary>
/// Phase-4b B3b refactor:
/// - UploadFileVersionCommand.FileRecordId is Guid (PublicId) — resolve to long FK via IFileRepository.
/// - FileVersion is T3 plain-class: InternalId (long PK) + MessageId (Guid AK).
/// - IRepository uses long TId for T3 entities.
/// - Dapper MAX(VersionNumber) query uses resolved long FileRecordId for correct FK match.
/// </summary>
public sealed class UploadFileVersionCommandHandler(
    IFileRepository fileRepository,
    IRepository<FileVersion, long> repository,
    IGuidGenerator guidGen,
    IReadDbConnection db)
    : IRequestHandler<UploadFileVersionCommand, Result<FileVersionDto>>
{
    public async Task<Result<FileVersionDto>> Handle(
        UploadFileVersionCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve FileRecord PublicId → internal long FK
        var fileResult = await fileRepository.GetByIdAsync(
            FileId.From(request.FileRecordId), cancellationToken);

        if (fileResult.IsFailed)
            return Result.Fail(new NotFoundError($"FileRecord {request.FileRecordId} not found."));

        var fileRecordInternalId = fileResult.Value.Id;

        // Get next version number for this file record — use long FK column
        const string sql = """
            SELECT ISNULL(MAX(VersionNumber), 0) + 1
            FROM files.FileVersions
            WHERE FileRecordId = @FileRecordId
            """;

        var nextVersion = await db.QuerySingleAsync<int>(
            sql, new { FileRecordId = fileRecordInternalId }, cancellationToken);

        var version = FileVersion.Create(
            guidGen,
            fileRecordInternalId,
            nextVersion,
            request.StoragePath,
            request.FileSize,
            request.ContentHash,
            request.UploadedBy,
            request.TenantId,
            request.Comment);

        await repository.AddAsync(version, cancellationToken);

        return Result.Ok(MapToDto(version));
    }

    // Phase-4b B3b: T3 DTO uses MessageId (Guid AK) and long FileRecordId.
    internal static FileVersionDto MapToDto(FileVersion v) => new(
        v.MessageId, v.FileRecordId, v.VersionNumber, v.StoragePath,
        v.FileSize, v.ContentHash, v.UploadedBy, v.UploadedAt,
        v.Comment, v.TenantId);
}
