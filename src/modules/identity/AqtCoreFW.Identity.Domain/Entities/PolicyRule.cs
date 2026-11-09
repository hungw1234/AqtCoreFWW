using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>Determines whether a matching rule grants or denies access.</summary>
public enum PolicyEffect
{
    Allow = 1,
    Deny = 2
}

/// <summary>
/// A policy rule that evaluates a condition expression against a permission code.
/// Rules are sorted by Priority (desc) — first match wins.
/// Deny takes precedence when it is the first match.
/// T1: long PK (Id, DB-assigned IDENTITY).
/// </summary>
public class PolicyRule : Entity
{
    /// <summary>Stable machine-readable code. Unique across tenant.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Permission code this rule applies to, e.g. "HOSO.HOSO.APPROVE".</summary>
    public string PermissionCode { get; set; } = string.Empty;

    /// <summary>
    /// JSON condition array. Format: [{"field":"X","op":"eq","value":"Y"}].
    /// Supported ops: eq | neq | in | notin. All conditions are ANDed.
    /// Null/empty = unconditional (always matches).
    /// </summary>
    public string? ConditionExpression { get; set; }

    public PolicyEffect Effect { get; set; } = PolicyEffect.Allow;

    /// <summary>Higher priority rules are evaluated first.</summary>
    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>When true, a Deny result is written to the audit log.</summary>
    public bool LogOnDeny { get; set; }

    public string? Description { get; set; }

    public Guid? TenantId { get; set; }
}
