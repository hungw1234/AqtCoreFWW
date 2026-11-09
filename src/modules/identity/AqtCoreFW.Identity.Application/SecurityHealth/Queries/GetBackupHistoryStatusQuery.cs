using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.SecurityHealth.Queries;

// ── Query ────────────────────────────────────────────────────────────────────

/// <summary>
/// QĐ 742 §6.1.b — D7 visibility: backup/restore-drill history.
/// Reads [backup].BackupRecords via Dapper (cross-schema read; same connection string).
/// Default page size 20 — callers show the latest runs first.
/// </summary>
public sealed record GetBackupHistoryStatusQuery(int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResult<BackupHistoryItemDto>>>;

public sealed class GetBackupHistoryStatusQueryValidator
    : AbstractValidator<GetBackupHistoryStatusQuery>
{
    public GetBackupHistoryStatusQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

/// <summary>Single backup/restore-drill record for the D7 visibility page.</summary>
public sealed record BackupHistoryItemDto(
    Guid            Id,
    /// <summary>"Backup" or "RestoreDrill"</summary>
    string          Type,
    /// <summary>"Pending", "InProgress", "Completed", "Failed"</summary>
    string          Status,
    string?         FilePath,
    long?           FileSizeBytes,
    string?         ErrorMessage,
    Guid            TriggeredBy,
    DateTimeOffset  StartedAt,
    DateTimeOffset? CompletedAt);

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetBackupHistoryStatusQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetBackupHistoryStatusQuery, Result<PagedResult<BackupHistoryItemDto>>>
{
    public async Task<Result<PagedResult<BackupHistoryItemDto>>> Handle(
        GetBackupHistoryStatusQuery request, CancellationToken ct)
    {
        var offset = (request.Page - 1) * request.PageSize;

        const string sql = """
            SELECT
                b.Id, b.Type, b.Status,
                b.FilePath, b.FileSizeBytes, b.ErrorMessage,
                b.TriggeredBy, b.StartedAt, b.CompletedAt,
                COUNT(*) OVER() AS TotalCount
            FROM [backup].BackupRecords b
            ORDER BY b.StartedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await db.QueryAsync<BackupHistoryRow>(
            sql, new { Offset = offset, request.PageSize });

        var list = rows.ToList();
        var total = list.Count > 0 ? list[0].TotalCount : 0;

        var dtos = list.Select(r => new BackupHistoryItemDto(
            r.Id, r.Type, r.Status,
            r.FilePath, r.FileSizeBytes, r.ErrorMessage,
            r.TriggeredBy, r.StartedAt, r.CompletedAt)).ToList();

        var totalPages = request.PageSize > 0
            ? (int)Math.Ceiling((double)total / request.PageSize)
            : 0;
        var meta = new PaginationMeta(
            request.Page, request.PageSize, totalPages,
            null, null, request.Page < totalPages);

        return Result.Ok(new PagedResult<BackupHistoryItemDto>(dtos, total, meta));
    }
}

internal sealed record BackupHistoryRow(
    Guid Id, string Type, string Status,
    string? FilePath, long? FileSizeBytes, string? ErrorMessage,
    Guid TriggeredBy, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    int TotalCount);
