namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for OrgUnit lookups.
/// Monolith: InProcessOrgUnitModuleClient (direct DB query).
/// Microservice: HttpOrgUnitModuleClient (when module extracted — Phase 2+).
/// </summary>
public interface IOrgUnitModuleClient
{
    /// <summary>Max IDs per batch call. Exceeding throws ArgumentException.</summary>
    const int MaxBatchSize = 5000;

    /// <summary>
    /// Batch lookup org unit info by IDs. Returns only found units (missing IDs omitted).
    /// Chunked at 500 IDs internally. Max 5000 IDs per call.
    /// </summary>
    /// <param name="ids">OrgUnit IDs to look up</param>
    /// <param name="tenantId">Optional tenant filter for data isolation [RT-02]</param>
    Task<IReadOnlyDictionary<Guid, OrgUnitInfoDto>> GetOrgUnitsByIdsAsync(
        IEnumerable<Guid> ids, Guid? tenantId = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the root OrgUnit (ParentId IS NULL AND Level = 1) for a single tenant, or null when missing.
    /// Used by Identity.ListTenants refactor (replaces cross-schema JOIN).
    /// </summary>
    Task<OrgUnitSummary?> GetRootOrgUnitForTenantAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Batch variant of <see cref="GetRootOrgUnitForTenantAsync"/>.
    /// Returns dict keyed by TenantId; missing tenants omitted. Single SQL roundtrip.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, OrgUnitSummary>> GetRootOrgUnitsForTenantsAsync(
        IReadOnlyList<Guid> tenantIds, CancellationToken ct = default);

    /// <summary>
    /// Recursively expand an OrgUnit subtree, scoped to a specific tenant.
    /// Returns ALL descendant IDs INCLUDING the root.
    /// Single roundtrip via SQL recursive CTE filtered on TenantId — defense-in-depth
    /// against cross-tenant subtree leakage (callers' RoleDataScope rows are not
    /// trusted as the sole tenant boundary).
    /// Used by Identity.DataScopeService for ORG_TREE scope resolution.
    /// </summary>
    Task<IReadOnlyList<Guid>> ExpandSubtreeAsync(
        Guid rootId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// RT2-15 Phase 04 — PRODUCER: resolves all UserIds with an ACTIVE TEMPORAL assignment
    /// to any org unit whose Code is in <paramref name="codes"/> within <paramref name="tenantId"/>.
    ///
    /// Temporal filter: <c>IsActive=1 AND ValidFrom &lt;= NOW AND (ValidTo IS NULL OR ValidTo &gt; NOW)</c>.
    /// Flat (no descendants) — tree expansion deferred (RT2-15 FU).
    /// <c>DISTINCT UserId</c>: one user may hold multiple simultaneous active assignments.
    /// Empty <paramref name="codes"/> → returns empty list (short-circuit, no SQL).
    ///
    /// <b>INVARIANT 260525-E</b>: <paramref name="tenantId"/> is mandatory non-nullable.
    /// Cross-module client = security boundary; no ambient tenant escape hatch.
    ///
    /// Mirror of <see cref="IIdentityModuleClient.ResolveActiveUsersInGroupsAsync"/> (p112/p113).
    /// </summary>
    Task<IReadOnlyList<Guid>> ResolveActiveUsersInOrgUnitsAsync(
        IReadOnlyList<string> codes, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// RT2-15 Phase 04 — READER: returns the canonical (UPPER-trimmed) org unit codes for
    /// all org units to which <paramref name="userId"/> has an active temporal assignment
    /// within <paramref name="tenantId"/>.
    ///
    /// Temporal filter: <c>IsActive=1 AND ValidFrom &lt;= NOW AND (ValidTo IS NULL OR ValidTo &gt; NOW)</c>.
    /// SQL projection: <c>UPPER(LTRIM(RTRIM(o.Code)))</c> — canonical form mirrors
    /// <see cref="WorkflowGraphValidator"/> Save-time Trim+ToUpperInvariant canonicalization.
    ///
    /// <b>INVARIANT 260525-E</b>: <paramref name="tenantId"/> is mandatory non-nullable.
    /// Cross-module client = security boundary; no ambient tenant escape hatch.
    ///
    /// Mirror of <see cref="IIdentityModuleClient.GetUserGroupCodesAsync"/> (p113).
    /// </summary>
    Task<IReadOnlySet<string>> GetUserOrgUnitCodesAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// [RT-12] Class-based DTO for Dapper compatibility.
/// Lightweight org unit info for cross-module display/enrichment.
/// </summary>
public sealed class OrgUnitInfoDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
}

/// <summary>
/// [RT-12] Class-based DTO for Dapper compatibility.
/// Tenant-scoped root OrgUnit summary for cross-module aggregation.
/// </summary>
public sealed class OrgUnitSummary
{
    public Guid TenantId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
