using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.ListFiles;

/// <summary>
/// Lists files for a tenant with uploader display name and isDigitallySigned badge enriched from Identity module.
/// Cross-Module Layer 2 (Display) per architecture decision 2026-04-04 — single batch lookup, no N+1.
/// Pattern mirrors Collaboration.GetMessagesQueryHandler.
/// </summary>
public sealed class ListFilesQueryHandler(
    IReadDbConnection db,
    IIdentityModuleClient identityClient,
    ISigningRequestClient signingClient)
    : IRequestHandler<ListFilesQuery, Result<ListFilesResult>>
{
    public async Task<Result<ListFilesResult>> Handle(
        ListFilesQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(
            request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var skip = (page - 1) * pageSize;

        const string countSql =
            "SELECT COUNT(*) FROM [files].FileRecords WHERE TenantId = @TenantId AND IsDeleted = 0";
        var totalCount = await db.QuerySingleAsync<int>(countSql,
            new { request.TenantId }, cancellationToken);

        // Phase-4b B3b: T2 entity — project PublicId AS Id (FileRecords.Id is bigint after re-tier).
        // PascalCase aliases — FE FileRecordDto reads response fields directly (no camelCase remap).
        const string sql = """
            SELECT PublicId AS Id, OriginalFileName, ContentType, SizeBytes,
                   CASE Status
                     WHEN 0 THEN 'Pending'
                     WHEN 1 THEN 'Clean'
                     WHEN 2 THEN 'Infected'
                     ELSE 'Failed'
                   END AS VirusScanStatus,
                   UploadedBy, CreatedAt AS UploadedAt
            FROM [files].FileRecords
            WHERE TenantId = @TenantId AND IsDeleted = 0
            ORDER BY CreatedAt DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            """;

        var rows = (await db.QueryAsync<FileListRow>(sql,
            new { request.TenantId, Skip = skip, Take = pageSize }, cancellationToken)).ToList();

        // Cross-Module Layer 2 (Display): batch-resolve uploader names via Identity client
        var uploaderIds = rows.Select(r => r.UploadedBy).Distinct();
        var userMap = await identityClient.GetUserInfoByIdsAsync(
            uploaderIds, request.TenantId, cancellationToken);

        // Cross-Module Layer 2 (Display): batch-resolve which files have ≥1 Completed signed step.
        // Single query to Identity — no per-row N+1. Files with no signing request → not in the set.
        var fileIds = rows.Select(r => r.Id).ToList();
        var signedFileIds = await signingClient.GetSignedFileIdsAsync(
            request.TenantId, fileIds, cancellationToken);

        var items = rows
            .Select(r => (object)new
            {
                r.Id,
                r.OriginalFileName,
                r.ContentType,
                r.SizeBytes,
                r.VirusScanStatus,
                r.UploadedBy,
                r.UploadedAt,
                UploadedByName = userMap.TryGetValue(r.UploadedBy, out var u)
                    ? (u.FullName ?? u.Email)
                    : null,
                IsDigitallySigned = signedFileIds.Contains(r.Id),
            })
            .ToList();

        return Result.Ok(new ListFilesResult(items, totalCount));
    }

    /// <summary>Dapper materialization row — class (not positional record) per Dapper nullable handling.</summary>
    private sealed class FileListRow
    {
        public Guid Id { get; init; }
        public string OriginalFileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public string VirusScanStatus { get; init; } = string.Empty;
        public Guid UploadedBy { get; init; }
        public DateTimeOffset UploadedAt { get; init; }
    }
}
