namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Lookup table for data scope types.
/// Codes: SELF | ASSIGNED | ORG_UNIT | ORG_TREE | CUSTOM_LIST | ALL | BY_FIELD
/// </summary>
public class DataScopeType
{
    public Guid Id { get; set; }

    /// <summary>Machine-readable code, e.g. "SELF", "ALL", "ORG_UNIT".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
