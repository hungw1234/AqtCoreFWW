using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Segregation of Duties conflict rule — defines pairs of permissions that
/// must not coexist on the same user. Severity indicates the risk level of
/// the conflict (informational/warning only — never hard-blocks assignment).
/// T1: long PK (Id, DB-assigned IDENTITY).
/// </summary>
public class SodConflictRule : Entity
{
    /// <summary>First permission code in the conflicting pair.</summary>
    public string PermissionCodeA { get; set; } = string.Empty;

    /// <summary>Second permission code in the conflicting pair.</summary>
    public string PermissionCodeB { get; set; } = string.Empty;

    /// <summary>Severity level of the conflict — Low, Medium, or High.</summary>
    public SodSeverity Severity { get; set; } = SodSeverity.Low;

    /// <summary>Human-readable explanation of why these permissions conflict.</summary>
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? TenantId { get; set; }
}

/// <summary>
/// Severity level for SoD conflicts. Pure metadata — does NOT affect check outcome.
/// All conflicts are surfaced as warnings; assignment proceeds after confirmation.
/// </summary>
public enum SodSeverity
{
    /// <summary>Low risk — informational only.</summary>
    Low = 1,

    /// <summary>Medium risk — review recommended.</summary>
    Medium = 2,

    /// <summary>High risk — requires explicit acknowledgment.</summary>
    High = 3
}
