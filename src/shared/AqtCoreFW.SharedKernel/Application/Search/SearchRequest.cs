namespace AqtCoreFW.SharedKernel.Application.Search;

/// <summary>
/// Transport-agnostic search request. Both SQL FTS and Elasticsearch adapters consume this.
/// Filters are key-value ONLY — no SQL syntax permitted. Adapters translate to backend-specific queries.
/// Cursor pagination is mandatory (stable under concurrent inserts; OFFSET unsupported by ES at scale).
/// </summary>
public record SearchRequest
{
    /// <summary>Free-text search term. Null = match-all (filtered only).</summary>
    public string? Query { get; init; }

    /// <summary>Target index / table name. E.g. "cases", "documents".</summary>
    public string IndexName { get; init; } = string.Empty;

    /// <summary>
    /// Key-value filters. Adapters translate these — callers MUST NOT embed SQL/ES syntax here.
    /// Example: { "status": "Open", "assignedUserId": "abc-123" }
    /// </summary>
    public Dictionary<string, string> Filters { get; init; } = new();

    /// <summary>
    /// Opaque base64-encoded cursor from previous SearchResult.NextCursor.
    /// Null = first page.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>Max results per page. Capped at 100 by adapters.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>Mandatory tenant isolation. Adapters always inject this into queries.</summary>
    public Guid TenantId { get; init; }
}
