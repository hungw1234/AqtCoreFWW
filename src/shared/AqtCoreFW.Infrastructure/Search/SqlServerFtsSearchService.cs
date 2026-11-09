using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Search;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Search;

/// <summary>
/// ISearchService backed by SQL Server Full-Text Search (CONTAINS / FREETEXT).
/// Uses IReadDbConnection (Dapper) — never EF Core on the read path.
/// Security: ALL filters injected via DynamicParameters — never string interpolation.
/// Cursor: base64-encoded JSON { "id": "...", "rank": 0 } for stable pagination.
/// Degraded mode: returns empty result (not failure) when FTS index is missing.
/// Query building is delegated to FtsQueryBuilder.
/// </summary>
public sealed class SqlServerFtsSearchService : ISearchService
{
    private readonly IReadDbConnection _db;
    private readonly ILogger<SqlServerFtsSearchService> _logger;

    public SqlServerFtsSearchService(
        IReadDbConnection db,
        ILogger<SqlServerFtsSearchService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<SearchResult<T>>> SearchAsync<T>(
        SearchRequest request,
        CancellationToken ct = default)
        where T : SearchDocument
    {
        if (string.IsNullOrWhiteSpace(request.IndexName))
            return Result.Fail<SearchResult<T>>("SearchRequest.IndexName is required.");

        if (request.TenantId == Guid.Empty)
            return Result.Fail<SearchResult<T>>("SearchRequest.TenantId is required.");

        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        try
        {
            var cursor = FtsQueryBuilder.DecodeCursor(request.Cursor);
            var sql = FtsQueryBuilder.BuildSearchSql(request, pageSize, cursor is not null);
            var parameters = FtsQueryBuilder.BuildParameters(request, pageSize, cursor);

            var rows = await _db.QueryAsync<T>(sql, parameters, ct);
            var hits = rows.ToList();

            string? nextCursor = null;
            if (hits.Count == pageSize)
            {
                // Encode cursor from last row id so next page can seek past it
                nextCursor = FtsQueryBuilder.EncodeCursor(hits[^1].Id);
            }

            // Count query (approximate — no OFFSET scan needed)
            var countSql = FtsQueryBuilder.BuildCountSql(request);
            var countParams = FtsQueryBuilder.BuildCountParameters(request);
            long total = 0;
            try
            {
                total = await _db.QuerySingleAsync<long>(countSql, countParams, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search count query failed for index {Index}; using hit count", request.IndexName);
                total = hits.Count;
            }

            return Result.Ok(new SearchResult<T>
            {
                Hits = hits,
                Total = total,
                NextCursor = nextCursor
            });
        }
        catch (Exception ex) when (IsFtsCatalogMissingError(ex))
        {
            // Degraded mode: FTS index not provisioned yet — return empty, do not throw
            _logger.LogWarning(
                "FTS index missing for table '{Index}'. Returning empty search result. " +
                "Create a FULLTEXT CATALOG and FULLTEXT INDEX to enable full-text search.",
                request.IndexName);

            return Result.Ok(new SearchResult<T> { Hits = [], Total = 0 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL FTS search failed for index {Index}", request.IndexName);
            return Result.Fail<SearchResult<T>>($"Search failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<Result> IndexAsync<T>(T document, CancellationToken ct = default)
        where T : SearchDocument
    {
        // SQL FTS population is automatic via change tracking on the FT catalog.
        // This method is a no-op — callers persist via EF Core and FTS auto-updates.
        _logger.LogDebug(
            "SqlServerFtsSearchService.IndexAsync is a no-op — FTS auto-populates from EF Core writes.");
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> RemoveAsync(string indexName, string documentId, CancellationToken ct = default)
    {
        // SQL FTS removes entries automatically when the row is deleted.
        _logger.LogDebug(
            "SqlServerFtsSearchService.RemoveAsync is a no-op — FTS removes entries on row delete.");
        return Task.FromResult(Result.Ok());
    }

    // --- Error detection ---

    private static bool IsFtsCatalogMissingError(Exception ex)
    {
        // SQL Server error 7601: Cannot use a CONTAINS or FREETEXT predicate on table/view because it is not full-text indexed.
        // SQL Server error 7604: Full-text operation failed because the full-text catalog is not yet created.
        var msg = ex.Message;
        return msg.Contains("7601", StringComparison.Ordinal)
            || msg.Contains("7604", StringComparison.Ordinal)
            || msg.Contains("full-text", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("CONTAINS", StringComparison.OrdinalIgnoreCase);
    }
}
