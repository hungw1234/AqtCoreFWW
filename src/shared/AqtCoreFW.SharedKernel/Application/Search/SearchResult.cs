namespace AqtCoreFW.SharedKernel.Application.Search;

/// <summary>
/// Paginated search result with cursor for stable pagination.
/// NextCursor is null when no more pages exist.
/// </summary>
public sealed record SearchResult<T> where T : SearchDocument
{
    /// <summary>Matched documents for this page.</summary>
    public IReadOnlyList<T> Hits { get; init; } = [];

    /// <summary>Total matching documents across all pages (estimated for ES).</summary>
    public long Total { get; init; }

    /// <summary>
    /// Opaque base64 cursor. Pass as SearchRequest.Cursor to fetch next page.
    /// Null when this is the last page.
    /// </summary>
    public string? NextCursor { get; init; }

    /// <summary>True when more results exist beyond this page.</summary>
    public bool HasNextPage => NextCursor is not null;
}
