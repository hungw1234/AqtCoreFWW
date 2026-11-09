namespace AqtCoreFW.Identity.Domain.ValueObjects;

/// <summary>
/// Tenant lifecycle status. Stored as int in database.
/// Active = fully operational; Suspended = read-only access; Archived = soft-deleted (invisible by default).
/// </summary>
public enum TenantStatus
{
    /// <summary>Tenant is fully active and operational.</summary>
    Active = 1,

    /// <summary>Tenant is suspended — users can view but not mutate data.</summary>
    Suspended = 2,

    /// <summary>Tenant is archived (soft-deleted) — hidden from default lists.</summary>
    Archived = 3
}
