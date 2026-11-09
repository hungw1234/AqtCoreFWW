using FluentResults;

namespace AqtCoreFW.SharedKernel.Application.Search;

/// <summary>
/// Transport-agnostic search abstraction.
/// Backed by SqlFtsSearchService (default) or ElasticsearchSearchService (config switch).
/// All methods enforce tenant isolation via SearchRequest.TenantId.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Full-text search with cursor pagination.
    /// Returns Result.Fail when the search backend is unavailable.
    /// Returns empty Hits (not failure) when FTS index is missing — degraded mode.
    /// </summary>
    Task<Result<SearchResult<T>>> SearchAsync<T>(
        SearchRequest request,
        CancellationToken ct = default)
        where T : SearchDocument;

    /// <summary>
    /// Upsert a document into the search index.
    /// SQL adapter: triggers FTS population; ES adapter: PUT /{index}/_doc/{id}.
    /// </summary>
    Task<Result> IndexAsync<T>(T document, CancellationToken ct = default)
        where T : SearchDocument;

    /// <summary>
    /// Remove a document from the index by id.
    /// Idempotent — returns Ok even if document did not exist.
    /// </summary>
    Task<Result> RemoveAsync(string indexName, string documentId, CancellationToken ct = default);
}
