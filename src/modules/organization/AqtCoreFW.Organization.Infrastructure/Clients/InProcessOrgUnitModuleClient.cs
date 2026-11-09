using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Dapper;

namespace AqtCoreFW.Organization.Infrastructure.Clients;

/// <summary>
/// Monolith in-process adapter for IOrgUnitModuleClient.
/// Direct Dapper query against organization.OrgUnits.
/// OrgUnit is T2: external Id = PublicId (Guid); internal Id = long surrogate.
/// All SQL selects PublicId AS Id (or ParentPublicId AS ParentId) to match OrgUnitInfoDto/OrgUnitSummary Guid fields.
/// Swap to HttpOrgUnitModuleClient when module extracted (Phase 2+).
///
/// RT2-15 Phase 04: extended primary-ctor with <see cref="TimeProvider"/> for temporal
/// predicate injection (Q4 ratification). Tests substitute FakeTimeProvider via DI override.
/// </summary>
public sealed class InProcessOrgUnitModuleClient(IReadDbConnection db, TimeProvider timeProvider) : IOrgUnitModuleClient
{
    private const int ChunkSize = 500;

    /// <summary>
    /// Batch lookup org unit info by PublicIds with chunking.
    /// [RT-02] Optional tenant filter. [RT-09] Hard cap 5000 IDs.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, OrgUnitInfoDto>> GetOrgUnitsByIdsAsync(
        IEnumerable<Guid> ids, Guid? tenantId = null, CancellationToken ct = default)
    {
        var idList = ids?.Distinct().ToList();
        if (idList is null || idList.Count == 0)
            return new Dictionary<Guid, OrgUnitInfoDto>();

        if (idList.Count > IOrgUnitModuleClient.MaxBatchSize)
            throw new ArgumentException(
                $"Batch size {idList.Count} exceeds maximum {IOrgUnitModuleClient.MaxBatchSize}.",
                nameof(ids));

        // OrgUnit is T2: PublicId = external Guid; ParentId (long) resolved to parent's PublicId.
        var sql = """
            SELECT ou.PublicId AS Id, ou.Name, p.PublicId AS ParentId
            FROM organization.OrgUnits ou
            LEFT JOIN organization.OrgUnits p ON p.Id = ou.ParentId
            WHERE ou.PublicId IN @Ids AND ou.IsDeleted = 0
            """;

        if (tenantId.HasValue)
            sql += " AND ou.TenantId = @TenantId";

        var result = new Dictionary<Guid, OrgUnitInfoDto>(idList.Count);

        foreach (var chunk in idList.Chunk(ChunkSize))
        {
            var parameters = new DynamicParameters();
            parameters.Add("Ids", chunk);
            if (tenantId.HasValue)
                parameters.Add("TenantId", tenantId.Value);

            // OrgUnitInfoDto is a class (not record) — safe for Dapper [RT-12]
            var rows = await db.QueryAsync<OrgUnitInfoDto>(sql, parameters, ct);
            foreach (var row in rows)
                result[row.Id] = row;
        }

        return result;
    }

    /// <summary>
    /// Single-tenant root lookup. Returns null when no root row exists yet (e.g. tenant
    /// seeded only with users, no OrgUnit hierarchy).
    /// </summary>
    public async Task<OrgUnitSummary?> GetRootOrgUnitForTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        // OrgUnitSummary.Id = PublicId (Guid)
        const string sql = """
            SELECT TOP 1 TenantId, PublicId AS Id, Name
            FROM organization.OrgUnits
            WHERE TenantId = @TenantId
              AND ParentId IS NULL
              AND Level = 1
              AND IsDeleted = 0
            """;

        var rows = await db.QueryAsync<OrgUnitSummary>(sql, new { TenantId = tenantId }, ct);
        return rows.FirstOrDefault();
    }

    /// <summary>
    /// Batch root lookup — single roundtrip; missing tenants omitted from result.
    /// Caller responsible for tenant authorization (this is a SystemAdmin / cross-tenant aggregation primitive).
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, OrgUnitSummary>> GetRootOrgUnitsForTenantsAsync(
        IReadOnlyList<Guid> tenantIds, CancellationToken ct = default)
    {
        if (tenantIds is null || tenantIds.Count == 0)
            return new Dictionary<Guid, OrgUnitSummary>();

        if (tenantIds.Count > IOrgUnitModuleClient.MaxBatchSize)
            throw new ArgumentException(
                $"Batch size {tenantIds.Count} exceeds maximum {IOrgUnitModuleClient.MaxBatchSize}.",
                nameof(tenantIds));

        // OrgUnitSummary.Id = PublicId (Guid)
        const string sql = """
            SELECT TenantId, PublicId AS Id, Name
            FROM organization.OrgUnits
            WHERE TenantId IN @TenantIds
              AND ParentId IS NULL
              AND Level = 1
              AND IsDeleted = 0
            """;

        var result = new Dictionary<Guid, OrgUnitSummary>(tenantIds.Count);
        foreach (var chunk in tenantIds.Distinct().Chunk(ChunkSize))
        {
            var rows = await db.QueryAsync<OrgUnitSummary>(sql, new { TenantIds = chunk }, ct);
            foreach (var row in rows)
                result[row.TenantId] = row;
        }
        return result;
    }

    /// <summary>
    /// Recursive CTE expanding a subtree by PublicId (Guid), returning PublicIds of all descendants.
    /// Seeds on PublicId with TenantId-filtered guard; expands via internal Id/ParentId (long).
    /// Cross-tenant root rejected at the seed — descendants inherit tenancy via ParentId chain.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ExpandSubtreeAsync(
        Guid rootId, Guid tenantId, CancellationToken ct = default)
    {
        // Seed by PublicId; recurse on internal long Id/ParentId; project PublicId back.
        const string sql = """
            WITH OrgTree AS (
                SELECT Id, PublicId FROM organization.OrgUnits
                WHERE PublicId = @rootId AND TenantId = @tenantId
                UNION ALL
                SELECT o.Id, o.PublicId FROM organization.OrgUnits o
                INNER JOIN OrgTree t ON o.ParentId = t.Id
            )
            SELECT PublicId FROM OrgTree
            """;

        var rows = await db.QueryAsync<Guid>(sql, new { rootId, tenantId }, ct);
        return rows.ToList();
    }

    // ─────────────────────────────────────────────────────────────────────
    // RT2-15 Phase 04 — temporal membership methods (Q3 + Q4 + 260525-E)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// RT2-15 Phase 04 PRODUCER: returns DISTINCT UserIds with an active temporal assignment
    /// to any org unit whose Code is in <paramref name="codes"/> within <paramref name="tenantId"/>.
    ///
    /// Temporal filter: IsActive=1 AND ValidFrom &lt;= @Now AND (ValidTo IS NULL OR ValidTo &gt; @Now).
    /// JOIN on o.Code IN @Codes (Q3: code-keyed, no Id). Mirror: IIdentityModuleClient.ResolveActiveUsersInGroupsAsync.
    /// INVARIANT 260525-E: tenantId mandatory, non-nullable — cross-module client = security boundary.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ResolveActiveUsersInOrgUnitsAsync(
        IReadOnlyList<string> codes, Guid tenantId, CancellationToken ct = default)
    {
        // Short-circuit before SQL to avoid "empty IN @Codes" Dapper failure.
        if (codes.Count == 0)
            return [];

        // @Now from injected TimeProvider (not GETUTCDATE()) — Q4 testability.
        var now = timeProvider.GetUtcNow();
        const string sql = """
            SELECT DISTINCT a.UserId
            FROM organization.UserOrgUnitAssignments a
            JOIN organization.OrgUnits o ON o.Id = a.OrgUnitId
            WHERE o.Code IN @Codes
              AND o.TenantId = @TenantId
              AND a.IsActive = 1
              AND a.ValidFrom <= @Now
              AND (a.ValidTo IS NULL OR a.ValidTo > @Now)
            """;

        var rows = await db.QueryAsync<Guid>(sql,
            new { Codes = codes, TenantId = tenantId, Now = now }, ct);
        return rows.ToList();
    }

    /// <summary>
    /// RT2-15 Phase 04 READER: returns canonical (UPPER-trimmed) org unit codes for all
    /// org units to which <paramref name="userId"/> has an active temporal assignment
    /// within <paramref name="tenantId"/>.
    ///
    /// SQL projection: UPPER(LTRIM(RTRIM(o.Code))) — canonical form mirrors
    /// WorkflowGraphValidator Save-time Trim+ToUpperInvariant canonicalization.
    /// INVARIANT 260525-E: tenantId mandatory, non-nullable — cross-module client = security boundary.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetUserOrgUnitCodesAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        // @Now from injected TimeProvider (not GETUTCDATE()) — Q4 testability.
        var now = timeProvider.GetUtcNow();
        const string sql = """
            SELECT DISTINCT UPPER(LTRIM(RTRIM(o.Code))) AS Code
            FROM organization.UserOrgUnitAssignments a
            JOIN organization.OrgUnits o ON o.Id = a.OrgUnitId
            WHERE a.UserId = @UserId
              AND o.TenantId = @TenantId
              AND a.IsActive = 1
              AND a.ValidFrom <= @Now
              AND (a.ValidTo IS NULL OR a.ValidTo > @Now)
            """;

        var rows = await db.QueryAsync<string>(sql,
            new { UserId = userId, TenantId = tenantId, Now = now }, ct);
        return new HashSet<string>(rows, StringComparer.Ordinal);
    }
}
