using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// User-level data scope override — grants a specific user a scope beyond their role-based scopes.
/// Supports expiry for temporary grants (e.g. acting officer coverage).
/// GrantedBy / Reason required for audit trail (QĐ742).
/// T1: long PK (Id, DB-assigned IDENTITY).
/// UserId stays Guid: FK to ApplicationUser (IdentityUser&lt;Guid&gt; framework-pinned PK).
/// DataScopeTypeId stays Guid: FK to DataScopeType (code-keyed lookup, Guid PK per AMEND-A1 OOS list).
/// </summary>
public class UserDataScopeOverride : Entity
{
    public Guid UserId { get; set; }
    public Guid DataScopeTypeId { get; set; }

    /// <summary>Used when ScopeType is BY_FIELD — the field name to filter on.</summary>
    public string? ScopeField { get; set; }

    /// <summary>Used when ScopeType is BY_FIELD — the field value to match.</summary>
    public string? ScopeValue { get; set; }

    /// <summary>Mandatory justification for audit purposes.</summary>
    public string Reason { get; set; } = string.Empty;

    public Guid GrantedBy { get; set; }

    /// <summary>Null = permanent grant; set to limit temporary access.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
    public DataScopeType DataScopeType { get; set; } = null!;
}
