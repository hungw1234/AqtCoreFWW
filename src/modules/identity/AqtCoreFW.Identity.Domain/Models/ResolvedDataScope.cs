namespace AqtCoreFW.Identity.Domain.Models;

/// <summary>
/// The fully-resolved data scope for a user after merging role scopes and user-level overrides.
/// Consumers apply this as a query filter (EF Where clause or Dapper WHERE fragment).
/// </summary>
public sealed class ResolvedDataScope
{
    /// <summary>True when any scope resolves to ALL — bypass all row filters.</summary>
    public bool IsAll { get; init; }

    /// <summary>Include rows owned by the requesting user (SELF scope).</summary>
    public bool IncludeSelf { get; init; }

    /// <summary>Include rows explicitly assigned to the requesting user (ASSIGNED scope).</summary>
    public bool IncludeAssigned { get; init; }

    /// <summary>Org unit IDs the user may see (ORG_UNIT / ORG_TREE expansion).</summary>
    public IReadOnlyList<Guid> OrgUnitIds { get; init; } = [];

    /// <summary>Field-level equality filters (BY_FIELD scope).</summary>
    public IReadOnlyList<FieldFilter> FieldFilters { get; init; } = [];

    /// <summary>Convenience: user may see everything.</summary>
    public static ResolvedDataScope AllAccess() => new() { IsAll = true };

    /// <summary>Convenience: user may see nothing (no scopes granted).</summary>
    public static ResolvedDataScope NoAccess() => new();
}

/// <summary>A single field=value equality filter contributed by a BY_FIELD scope.</summary>
public sealed record FieldFilter(string Field, string Value);
