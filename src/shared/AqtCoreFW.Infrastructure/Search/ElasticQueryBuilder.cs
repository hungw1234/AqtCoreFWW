using System.Text;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Application.Search;
using FluentResults;

namespace AqtCoreFW.Infrastructure.Search;

/// <summary>
/// Internal helper for building Elasticsearch/OpenSearch query bodies and cursor encoding.
/// Keeps ElasticsearchSearchService focused on HTTP I/O and ISearchService contract.
/// </summary>
internal static class ElasticQueryBuilder
{
    /// <summary>
    /// Builds an ES bool query with must (query_string) + filter (tenant_id + key-value filters).
    /// Uses search_after for cursor-based pagination.
    /// </summary>
    internal static object BuildSearchBody(SearchRequest request, int pageSize)
    {
        var filters = new List<object>
        {
            new { term = new Dictionary<string, object> { ["tenant_id"] = request.TenantId } }
        };

        foreach (var (key, value) in request.Filters)
        {
            filters.Add(new { term = new Dictionary<string, object> { [key] = value } });
        }

        object query;
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            query = new
            {
                @bool = new
                {
                    must = new object[] { new { query_string = new { query = EscapeEsQuery(request.Query) } } },
                    filter = filters
                }
            };
        }
        else
        {
            query = new { @bool = new { filter = filters } };
        }

        var body = new Dictionary<string, object>
        {
            ["size"] = pageSize,
            ["query"] = query,
            ["sort"] = new object[] { new { _id = new { order = "asc" } } },
            ["track_total_hits"] = true
        };

        // Cursor (search_after)
        var cursor = DecodeCursor(request.Cursor);
        if (cursor is not null)
            body["search_after"] = cursor;

        return body;
    }

    internal static Result<SearchResult<T>> ParseSearchResponse<T>(string json, int pageSize)
        where T : SearchDocument
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var total = root
                .GetProperty("hits")
                .GetProperty("total")
                .GetProperty("value")
                .GetInt64();

            var hitsArray = root
                .GetProperty("hits")
                .GetProperty("hits")
                .EnumerateArray()
                .ToList();

            var items = new List<T>();
            foreach (var hit in hitsArray)
            {
                var source = hit.GetProperty("_source").GetRawText();
                var item = JsonSerializer.Deserialize<T>(source);
                if (item is not null) items.Add(item);
            }

            string? nextCursor = null;
            if (items.Count == pageSize && hitsArray.Count > 0)
            {
                var lastSort = hitsArray[^1].GetProperty("sort").GetRawText();
                nextCursor = EncodeCursor(lastSort);
            }

            return Result.Ok(new SearchResult<T>
            {
                Hits = items,
                Total = total,
                NextCursor = nextCursor
            });
        }
        catch (Exception ex)
        {
            return Result.Fail<SearchResult<T>>($"Failed to parse Elasticsearch response: {ex.Message}");
        }
    }

    // --- Security helpers ---

    /// <summary>
    /// Escapes Elasticsearch query_string special characters to prevent query injection.
    /// See: https://www.elastic.co/guide/en/elasticsearch/reference/current/query-dsl-query-string-query.html#_reserved_characters
    /// </summary>
    internal static string EscapeEsQuery(string term)
    {
        // Reserved: + - = && || > < ! ( ) { } [ ] ^ " ~ * ? : \ /
        var sb = new StringBuilder();
        foreach (var c in term)
        {
            if ("+-=&|><!(){}[]^\"~*?:\\/".Contains(c))
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    // --- Cursor encoding (search_after array) ---

    internal static string EncodeCursor(string sortJson)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(sortJson));
    }

    internal static object[]? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return JsonSerializer.Deserialize<object[]>(json);
        }
        catch
        {
            return null;
        }
    }
}
