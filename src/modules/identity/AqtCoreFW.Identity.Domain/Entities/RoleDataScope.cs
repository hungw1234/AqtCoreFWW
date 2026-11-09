namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Assigns a data scope type to a role, controlling what data rows that role can see.
/// Priority: higher value = evaluated first when resolving union of scopes.
/// </summary>
public class RoleDataScope
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public Guid DataScopeTypeId { get; set; }

    /// <summary>Used when ScopeType is BY_FIELD — the field name to filter on.</summary>
    public string? ScopeField { get; set; }

    /// <summary>Used when ScopeType is BY_FIELD — the field value to match.</summary>
    public string? ScopeValue { get; set; }

    public int Priority { get; set; } = 0;

    // Navigation
    public ApplicationRole Role { get; set; } = null!;
    public DataScopeType DataScopeType { get; set; } = null!;
}
