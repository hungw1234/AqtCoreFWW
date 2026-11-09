using System.Text.RegularExpressions;

namespace AqtCoreFW.Reporting.Application.Abstractions;

/// <summary>
/// Shared SQL validation for report templates and saved queries.
/// Blocks dangerous DDL/DML keywords, SQL comments, enforces @TenantId presence.
/// H-03: Added comment blocking (-- and /* */) to prevent keyword obfuscation.
/// </summary>
public static class SqlValidationHelper
{
    // ReDoS defense-in-depth belt. The blocklist pattern below is a flat alternation with no nested
    // quantifiers, so it does not backtrack catastrophically — this timeout is a safety net, not a
    // load-bearing bound. It is 1s (not a tight 100ms) so ordinary matches never false-fire under
    // CPU contention (parallel test assemblies, or a loaded prod host), which would otherwise make
    // FindDangerousKeyword return "PATTERN_TIMEOUT" instead of the real verdict. A genuine ReDoS
    // hangs for seconds/minutes, so 1s still trips it.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Expanded blocklist: DDL, DML, batch separators, dangerous functions, SQL comments.
    /// Covers UNION for injection, DECLARE/SET for variable attacks,
    /// WAITFOR for timing attacks, OPENROWSET/BULK for file access.
    /// H-03: Added -- (line comment) and /* (block comment) patterns.
    /// </summary>
    private static readonly Regex DangerousKeywordPattern = new(
        // E-02: Added SET to blocklist — prevents SET-based variable injection attacks
        @"\b(DROP|DELETE|UPDATE|INSERT|EXEC|EXECUTE|xp_|sp_|TRUNCATE|ALTER|CREATE|MERGE|GRANT|REVOKE|UNION|DECLARE|SET|WAITFOR|OPENROWSET|BULK\s+INSERT|INTO\s+OUTFILE)\b|;|--|/\*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Returns matched dangerous keyword, or null if SQL is safe.</summary>
    public static string? FindDangerousKeyword(string sql)
    {
        try
        {
            // H-03: Strip comments first, then check for dangerous keywords in cleaned SQL
            var cleaned = StripComments(sql);
            var match = DangerousKeywordPattern.Match(cleaned);
            if (match.Success) return match.Value;

            // Also check original SQL for comment markers themselves (defense-in-depth)
            if (sql.Contains("--") || sql.Contains("/*"))
                return "SQL_COMMENT";

            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            // ReDoS defense: treat timeout as dangerous input — reject immediately.
            // Secondary defense: Dapper parameterization prevents actual SQL injection.
            return "PATTERN_TIMEOUT";
        }
    }

    // E-07: Pre-compiled Regex instances with the shared RegexTimeout — prevents ReDoS on pathological input
    private static readonly Regex LineCommentPattern = new(
        @"--.*$",
        RegexOptions.Multiline | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex BlockCommentPattern = new(
        @"/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>Strips SQL line comments (--) and block comments (/* */).</summary>
    public static string StripComments(string sql)
    {
        try
        {
            // Remove line comments then block comments
            sql = LineCommentPattern.Replace(sql, "");
            sql = BlockCommentPattern.Replace(sql, "");
            return sql;
        }
        catch (RegexMatchTimeoutException)
        {
            // ReDoS defense — treat as dangerous input
            return string.Empty;
        }
    }

    /// <summary>Validates SQL must start with SELECT (whitelist approach).</summary>
    public static bool StartsWithSelect(string sql) =>
        sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Validates SQL contains @TenantId parameter.</summary>
    public static bool ContainsTenantIdParam(string sql) =>
        sql.Contains("@TenantId", StringComparison.OrdinalIgnoreCase);
}
