using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.GetFileVersions;

/// <summary>
/// Phase-4b B3b: FileVersion is T3 plain-class — InternalId (long PK), MessageId (Guid AK).
/// FileRecordId is now long (FK to FileRecord.Id bigint).
/// GetFileVersionsQuery.FileRecordId is Guid (the FileRecord PublicId) — resolve via JOIN.
/// SELECT MessageId AS MessageId, FileRecordId (long), etc.
/// </summary>
public sealed class GetFileVersionsQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetFileVersionsQuery, Result<IReadOnlyList<FileVersionDto>>>
{
    public async Task<Result<IReadOnlyList<FileVersionDto>>> Handle(
        GetFileVersionsQuery request,
        CancellationToken cancellationToken)
    {
        // Join FileRecords to resolve PublicId → long FileRecordId for the WHERE filter.
        // Project MessageId (Guid AK) as the public identifier, not InternalId.
        const string sql = """
            SELECT fv.MessageId, fv.FileRecordId, fv.VersionNumber, fv.StoragePath, fv.FileSize,
                   fv.ContentHash, fv.UploadedBy, fv.UploadedAt, fv.Comment, fv.TenantId
            FROM files.FileVersions fv
            INNER JOIN files.FileRecords fr ON fr.Id = fv.FileRecordId
            WHERE fr.PublicId = @FileRecordPublicId
              AND fv.TenantId = @TenantId
            ORDER BY fv.VersionNumber
            """;

        var rows = await db.QueryAsync<FileVersionDto>(
            sql,
            new { FileRecordPublicId = request.FileRecordId, request.TenantId },
            cancellationToken);

        return Result.Ok<IReadOnlyList<FileVersionDto>>(rows.ToList());
    }
}
