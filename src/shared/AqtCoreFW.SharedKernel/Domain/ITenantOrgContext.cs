namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>
/// Provides current user's org unit context from JWT claim "org_unit_id".
/// Used by modules to filter data by org unit hierarchy.
/// </summary>
public interface ITenantOrgContext
{
    /// <summary>Current user's primary org unit (from JWT claim "org_unit_id"). Null if not set.</summary>
    Guid? CurrentOrgUnitId { get; }

    /// <summary>Returns true if the given orgUnitId is the current unit or an ancestor.</summary>
    bool IsInOrgUnit(Guid orgUnitId);

    /// <summary>Returns [current, parent, grandparent, ...] from current unit to root.</summary>
    IReadOnlyList<Guid> GetOrgUnitHierarchy();
}
