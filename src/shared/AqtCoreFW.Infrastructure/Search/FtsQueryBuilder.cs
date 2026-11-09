using System.Text;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Application.Search;
using Dapper;

namespace AqtCoreFW.Infrastructure.Search;

/// <summary>
/// Internal helper for building SQL Server FTS query strings and parameter sets.
/// All SQL identifiers are sanitized via whitelist — no string interpolation from user input.
/// </summary>
internal static class FtsQueryBuilder
{
    // FTS special characters that must be escaped before injection into CONTAINS()
    private static readonly char[] FtsSpecialChars = ['"', '~', '*', '&', '|', '!', '(', ')', '{', '}', '-', '+'];

    /// <summary>
    /// Builds a CONTAINS-based SELECT with cursor seek (keyset pagination).
    /// Table name is validated against IndexName — only alphanumeric + underscore allowed.
    /// </summary>
    internal static string BuildSearchSql(SearchRequest request, int pageSize, bool hasCursor)
    {
        var safeIndex = SanitizeIdentifier(request.IndexName);

        var sb = new StringBuilder();
        sb.AppendLine($"SELECT TOP (@PageSize) *");
        sb.AppendLine($"FROM search.{safeIndex}_v");
        sb.AppendLine("WHERE tenant_id = @TenantId");

        // Cursor seek: WHERE id > @AfterId for stable keyset pagination
        if (hasCursor)
            sb.AppendLine("  AND id > @AfterId");

        // FTS predicate — only when a query term is provided
        if (!string.IsNullOrWhiteSpace(request.Query))
            sb.AppendLine("  AND CONTAINS(search_text, @FtsQuery)");

        // Key-value filters — each key maps to a column; validated before append
        foreach (var key in request.Filters.Keys)
        {
            var safeCol = SanitizeIdentifier(key);
            sb.AppendLine($"  AND {safeCol} = @filter_{safeCol}");
        }

        sb.AppendLine("ORDER BY id");

        return sb.ToString();
    }

    internal static string BuildCountSql(SearchRequest request)
    {
        var safeIndex = SanitizeIdentifier(request.IndexName);
        var sb = new StringBuilder();
        sb.AppendLine($"SELECT COUNT_BIG(*)");
        sb.AppendLine($"FROM search.{safeIndex}_v");
        sb.AppendLine("WHERE tenant_id = @TenantId");

        if (!string.IsNullOrWhiteSpace(request.Query))
            sb.AppendLine("  AND CONTAINS(search_text, @FtsQuery)");

        foreach (var key in request.Filters.Keys)
        {
            var safeCol = SanitizeIdentifier(key);
            sb.AppendLine($"  AND {safeCol} = @filter_{safeCol}");
        }

        return sb.ToString();
    }

    internal static DynamicParameters BuildParameters(SearchRequest request, int pageSize, CursorToken? cursor)
    {
        var p = new DynamicParameters();
        p.Add("@TenantId", request.TenantId);
        p.Add("@PageSize", pageSize);

        if (!string.IsNullOrWhiteSpace(request.Query))
            p.Add("@FtsQuery", EscapeFtsQuery(request.Query));

        if (cursor is not null)
            p.Add("@AfterId", cursor.Id);

        foreach (var (key, value) in request.Filters)
        {
            var safeCol = SanitizeIdentifier(key);
            p.Add($"@filter_{safeCol}", value);
        }

        return p;
    }

    internal static DynamicParameters BuildCountParameters(SearchRequest request)
    {
        var p = new DynamicParameters();
        p.Add("@TenantId", request.TenantId);

        if (!string.IsNullOrWhiteSpace(request.Query))
            p.Add("@FtsQuery", EscapeFtsQuery(request.Query));

        foreach (var (key, value) in request.Filters)
        {
            var safeCol = SanitizeIdentifier(key);
            p.Add($"@filter_{safeCol}", value);
        }

        return p;
    }

    // --- Security helpers ---

    /// <summary>
    /// Whitelist identifier characters: letters, digits, underscore only.
    /// Prevents SQL injection via index name or filter key.
    /// </summary>
    internal static string SanitizeIdentifier(string input)
    {
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("Identifier must not be empty.");

        foreach (var c in input)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException($"Invalid identifier character '{c}' in '{input}'. Only letters, digits, underscores allowed.");
        }

        return input;
    }

    /// <summary>
    /// Escapes FTS special characters so user input cannot break CONTAINS() syntax.
    /// Wraps the term in double-quotes for phrase search after escaping.
    /// </summary>
    internal static string EscapeFtsQuery(string term)
    {
        // Strip FTS special chars then wrap as phrase
        var sanitized = new StringBuilder();
        foreach (var c in term)
        {
            if (Array.IndexOf(FtsSpecialChars, c) < 0)
                sanitized.Append(c);
        }

        var clean = sanitized.ToString().Trim();
        if (string.IsNullOrEmpty(clean))
            return "\"\"";

        // Phrase search: "nguyễn văn a" matches as whole phrase with Vietnamese tokenizer
        return $"\"{clean}\"";
    }

    // --- Cursor encoding ---

    internal sealed record CursorToken(string Id);

    internal static string? EncodeCursor(string lastId)
    {
        var token = JsonSerializer.Serialize(new CursorToken(lastId));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
    }

    internal static CursorToken? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return JsonSerializer.Deserialize<CursorToken>(json);
        }
        catch
        {
            // Invalid cursor — treat as first page
            return null;
        }
    }
}
