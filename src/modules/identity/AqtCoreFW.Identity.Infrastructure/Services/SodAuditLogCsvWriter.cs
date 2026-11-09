namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// CSV writer for SoD violation audit logs.
/// Implements RFC 4180 escape + OWASP formula-injection defense for ALL string cells.
/// </summary>
public static class SodAuditLogCsvWriter
{
    private static readonly char[] FormulaStartChars = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>CSV header row — order must match WriteRow().</summary>
    public static void WriteHeader(TextWriter writer)
    {
        writer.WriteLine("DetectedAt,UserId,UserName,RoleId,RoleName,RuleId,PermissionCodeA,PermissionCodeB,Severity,ActorId,ActorName,Justification");
    }

    /// <summary>Write a single row to CSV. All string cells are escaped per RFC 4180 + OWASP.</summary>
    public static void WriteRow(
        TextWriter writer,
        DateTime detectedAt,
        Guid? userId,
        string? userName,
        Guid? roleId,
        string? roleName,
        long? ruleId,
        string permissionCodeA,
        string permissionCodeB,
        string severity,
        Guid actorId,
        string? actorName,
        string? justification)
    {
        // ISO8601 UTC for DetectedAt
        writer.Write(detectedAt.ToString("o"));
        writer.Write(',');
        writer.Write(userId?.ToString() ?? string.Empty);
        writer.Write(',');
        writer.Write(EscapeCell(userName));
        writer.Write(',');
        writer.Write(roleId?.ToString() ?? string.Empty);
        writer.Write(',');
        writer.Write(EscapeCell(roleName));
        writer.Write(',');
        writer.Write(ruleId?.ToString() ?? string.Empty);
        writer.Write(',');
        writer.Write(EscapeCell(permissionCodeA));
        writer.Write(',');
        writer.Write(EscapeCell(permissionCodeB));
        writer.Write(',');
        writer.Write(EscapeCell(severity));
        writer.Write(',');
        writer.Write(actorId);
        writer.Write(',');
        writer.Write(EscapeCell(actorName));
        writer.Write(',');
        writer.WriteLine(EscapeCell(justification));
    }

    /// <summary>
    /// Escape cell value per RFC 4180 + OWASP formula-injection defense.
    /// 1. If cell starts with = + - @ \t \r, prepend single quote ' (OWASP defense).
    /// 2. If cell contains comma, quote, or newline, wrap in double quotes + escape internal quotes.
    /// </summary>
    public static string EscapeCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var escaped = value;

        // OWASP formula-injection defense: prefix cells starting with formula chars
        if (value.Length > 0 && FormulaStartChars.Contains(value[0]))
        {
            escaped = "'" + value;
        }

        // RFC 4180: wrap in double quotes if contains comma, quote, or newline
        if (escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r'))
        {
            return $"\"{escaped.Replace("\"", "\"\"")}\"";
        }

        return escaped;
    }
}
