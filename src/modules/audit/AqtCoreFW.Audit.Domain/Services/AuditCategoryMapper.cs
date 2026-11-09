using System.Collections.Concurrent;
using System.Reflection;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Domain.Services;

/// <summary>
/// Maps a runtime audit action string (and optional command type) to the
/// QĐ 742 (<see cref="AuditCategory"/>, <see cref="AuditSeverity"/>) tuple
/// persisted on every <c>AuditLogEntry</c>.
///
/// Resolution order for <see cref="AuditCategory"/>:
///   1. Explicit <see cref="AuditCategoryAttribute"/> on the command type — wins.
///   2. Verb-based fallback for the well-known action strings emitted by
///      <c>AuditBehavior.ExtractAction</c> ("Create", "Update", "Delete", "Login",
///      "Logout", "Read", "Assign", "Revoke", "Sync", ...).
///   3. <see cref="AuditCategory.AppAccess"/> (safest default — caller is encouraged
///      to log a warning when no attribute / verb match is found).
///
/// <see cref="AuditSeverity"/> is always inferred from the verb. Runtime escalations
/// (repeated AccessDenied, lockout-on-active-session, sensitive RTBF, etc.) are
/// applied by call sites that have the additional context to detect them — the
/// mapper only encodes the static verb→severity baseline.
/// </summary>
public static class AuditCategoryMapper
{
    private static readonly ConcurrentDictionary<Type, AuditCategoryAttribute?> _attributeCache = new();

    /// <summary>
    /// Returns <c>(Category, Severity)</c> for the given action.
    /// </summary>
    /// <param name="action">
    /// Action string as recorded on <c>AuditLogEntry.Action</c>. Both verb-style values
    /// emitted by <c>AuditBehavior.ExtractAction</c> ("Create", "Update", ...) and
    /// SCREAMING_SNAKE_CASE constants from <see cref="AuditAction"/> ("LOGIN", "RTBF",
    /// "ROLE_ASSIGN") are accepted. Comparison is case-insensitive on the normalised form.
    /// </param>
    /// <param name="commandType">
    /// Optional MediatR command type. When supplied, an <see cref="AuditCategoryAttribute"/>
    /// (if present, including inherited) overrides the verb-based category fallback.
    /// </param>
    public static (AuditCategory Category, AuditSeverity Severity) Map(
        string action,
        Type? commandType = null)
    {
        var category = ResolveCategory(action, commandType);
        var severity = ResolveSeverity(action);
        return (category, severity);
    }

    private static AuditCategory ResolveCategory(string action, Type? commandType)
    {
        if (commandType is not null)
        {
            var attr = _attributeCache.GetOrAdd(
                commandType,
                static t => t.GetCustomAttribute<AuditCategoryAttribute>(inherit: true));
            if (attr is not null) return attr.Category;
        }

        return Normalise(action) switch
        {
            "LOGIN" or "LOGOUT" => AuditCategory.AppAccess,
            "READ" or "DATAACCESS" => AuditCategory.AppAccess,

            "ASSIGN" or "REVOKE" or "SYNC"
                or "ROLEASSIGN" or "ROLEREVOKE"
                or "ROLEASSIGNED" or "ROLEREVOKED"
                or "PERMISSIONGRANTED" or "PERMISSIONREVOKED"
                or "GROUPROLEASSIGNED" or "GROUPROLEREVOKED"
                or "DELEGATIONCREATED" or "DELEGATIONREVOKED"
                or "DATASCOPECHANGED"
                or "TOKENREVOKE"
                => AuditCategory.AccountManagement,

            "ACCESSDENIED" or "SENSITIVEACTIONUSED"
                => AuditCategory.Error,

            "RTBF" => AuditCategory.AccountManagement,

            _ => AuditCategory.AppAccess
        };
    }

    private static AuditSeverity ResolveSeverity(string action) => Normalise(action) switch
    {
        // Read-only or routine session traffic
        "READ" or "DATAACCESS"
            or "LOGIN" or "LOGOUT"
            or "MARK"
            => AuditSeverity.Low,

        // Mutations and account-management writes
        "CREATE" or "UPDATE" or "ASSIGN" or "REVOKE" or "SYNC"
            or "SUBMIT" or "APPROVE" or "REJECT" or "CLOSE" or "PUBLISH"
            or "RESET" or "LOCK" or "UNLOCK" or "RESTORE" or "REGISTER"
            or "SEND" or "INGEST" or "DELEGATE" or "TRIGGER" or "MANAGE" or "REPORT"
            or "EXPORT" or "IMPORT"
            or "ROLEASSIGN" or "ROLEREVOKE"
            or "ROLEASSIGNED" or "ROLEREVOKED"
            or "PERMISSIONGRANTED" or "PERMISSIONREVOKED"
            or "GROUPROLEASSIGNED" or "GROUPROLEREVOKED"
            or "DELEGATIONCREATED" or "DELEGATIONREVOKED"
            or "DATASCOPECHANGED"
            or "TOKENREVOKE"
            => AuditSeverity.Medium,

        // Destructive or admin-elevated operations
        "DELETE" => AuditSeverity.High,

        // Compliance-critical / breach-class events
        "RTBF" or "ACCESSDENIED" or "SENSITIVEACTIONUSED"
            or "CAROOTTHUMBPRINTSCHANGED"   // VN CA root pin change — high-impact security event (QĐ 742 §5.1)
            => AuditSeverity.Critical,

        _ => AuditSeverity.Low
    };

    // Underscores stripped so SCREAMING_SNAKE_CASE constants from AuditAction
    // and verb-style strings from AuditBehavior collapse to the same key.
    private static string Normalise(string action) =>
        string.IsNullOrWhiteSpace(action)
            ? string.Empty
            : action.Trim().Replace("_", string.Empty).ToUpperInvariant();
}
