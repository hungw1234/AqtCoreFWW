using AqtCoreFW.Organization.Application.DTOs;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Organization.Infrastructure.Services;

/// <summary>
/// Core org unit service: cache-aside tree, recursive CTE descendants, ancestor walk.
/// Cache key pattern: "org:tree:{tenantId}" (TTL 15min).
/// IMPORTANT: Key includes tenantId — prevents cross-tenant cache leaks.
/// OrgUnit is T2: external addressing via PublicId (Guid); internal FK via Id (long).
/// All public methods accept/return Guid = PublicId.
/// </summary>
public class OrgUnitService(
    OrgDbContext db,
    IReadDbConnection readDb,
    ICacheService cache)
{
    private static string TreeKey(Guid tenantId) => $"org:tree:{tenantId}";
    private static readonly TimeSpan TreeTtl = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<OrgUnitDto>> GetTreeAsync(Guid tenantId, CancellationToken ct = default)
    {
        var cached = await cache.GetAsync<List<OrgUnitDto>>(TreeKey(tenantId), ct);
        if (cached is not null)
            return cached;

        // Load raw units with computed child/staff counts via subquery.
        // Project PublicId as the external Id for DTO; ParentId/SuccessorId require JOIN
        // to resolve the parent's PublicId. For tree queries, we load all units then
        // resolve parent PublicIds in-memory (avoids N+1 SQL joins).
        var rawUnits = await db.Query<OrgUnit>()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Level).ThenBy(x => x.Code)
            .Select(x => new
            {
                x.Id,          // long — internal surrogate key
                x.PublicId,    // Guid — external addressing key
                x.ParentId,    // long? — internal FK
                x.Name, x.NameEn,
                x.Code, x.Level, x.IsActive, x.TenantId, x.SuccessorId,
                ChildCount = db.OrgUnits.Count(c => c.ParentId == x.Id && !c.IsDeleted),
                StaffCount = db.Assignments.Count(a => a.OrgUnitId == x.Id && a.IsActive && !a.IsDeleted),
            })
            .ToListAsync(ct);

        // Build Id→PublicId map for parent/successor resolution
        var idToPublicId = rawUnits.ToDictionary(x => x.Id, x => x.PublicId);

        var units = rawUnits.Select(x => new OrgUnitDto(
            x.PublicId,
            x.ParentId.HasValue && idToPublicId.TryGetValue(x.ParentId.Value, out var pp) ? pp : null,
            x.Name, x.NameEn,
            x.Code, x.Level, x.IsActive, x.TenantId,
            x.SuccessorId.HasValue && idToPublicId.TryGetValue(x.SuccessorId.Value, out var sp) ? sp : null,
            x.ChildCount, x.StaffCount)).ToList();

        await cache.SetAsync(TreeKey(tenantId), units, TreeTtl, ct);
        return units;
    }

    public async Task<IReadOnlyList<OrgUnitDto>> GetDescendantsAsync(
        Guid orgUnitPublicId, Guid tenantId, CancellationToken ct = default)
    {
        // CTE uses PublicId as seed, then joins on internal Id/ParentId (long).
        const string sql = """
            WITH OrgHierarchy AS (
                SELECT Id, PublicId, ParentId, Name, NameEn, Code, Level, IsActive, TenantId, SuccessorId
                FROM organization.OrgUnits
                WHERE PublicId = @RootPublicId AND TenantId = @TenantId AND IsDeleted = 0
                UNION ALL
                SELECT o.Id, o.PublicId, o.ParentId, o.Name, o.NameEn, o.Code, o.Level, o.IsActive, o.TenantId, o.SuccessorId
                FROM organization.OrgUnits o
                INNER JOIN OrgHierarchy h ON o.ParentId = h.Id
                WHERE o.TenantId = @TenantId AND o.IsDeleted = 0
            )
            SELECT * FROM OrgHierarchy;
            """;

        var rows = await readDb.QueryAsync<OrgUnitRow>(sql, new { RootPublicId = orgUnitPublicId, TenantId = tenantId }, ct);
        var rowList = rows.ToList();

        // Resolve parent PublicIds in-memory from the result set
        var idToPublicId = rowList.ToDictionary(r => r.Id, r => r.PublicId);
        return rowList.Select(r => new OrgUnitDto(
            r.PublicId,
            r.ParentId.HasValue && idToPublicId.TryGetValue(r.ParentId.Value, out var pp) ? pp : null,
            r.Name, r.NameEn, r.Code, r.Level, r.IsActive, r.TenantId,
            r.SuccessorId.HasValue && idToPublicId.TryGetValue(r.SuccessorId.Value, out var sp) ? sp : null))
                   .ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetAncestorsAsync(
        Guid orgUnitPublicId, Guid tenantId, CancellationToken ct = default)
    {
        var all = await GetTreeAsync(tenantId, ct);
        // OrgUnitDto.Id = PublicId
        var map = all.ToDictionary(x => x.Id);
        var result = new List<Guid>();

        var current = orgUnitPublicId;
        for (var depth = 0; depth < 20 && map.TryGetValue(current, out var node); depth++)
        {
            result.Add(node.Id);
            if (node.ParentId is null) break;
            current = node.ParentId.Value;
        }

        return result;
    }

    public async Task<OrgUnitDto?> GetByIdAsync(Guid publicId, Guid tenantId, CancellationToken ct = default)
    {
        // Load the entity by PublicId, then load parent for ParentId resolution (if needed).
        // Simpler: project PublicId, and resolve ParentPublicId via subquery or separate query.
        var raw = await db.Query<OrgUnit>()
            .Where(x => x.PublicId == publicId && x.TenantId == tenantId)
            .Select(x => new
            {
                x.PublicId, x.ParentId, x.Name, x.NameEn,
                x.Code, x.Level, x.IsActive, x.TenantId, x.SuccessorId
            })
            .FirstOrDefaultAsync(ct);

        if (raw is null) return null;

        // Resolve ParentPublicId and SuccessorPublicId if needed
        Guid? parentPublicId = null;
        Guid? successorPublicId = null;

        if (raw.ParentId.HasValue)
        {
            parentPublicId = await db.Query<OrgUnit>()
                .Where(x => x.Id == raw.ParentId.Value)
                .Select(x => (Guid?)x.PublicId)
                .FirstOrDefaultAsync(ct);
        }

        if (raw.SuccessorId.HasValue)
        {
            successorPublicId = await db.Query<OrgUnit>()
                .Where(x => x.Id == raw.SuccessorId.Value)
                .Select(x => (Guid?)x.PublicId)
                .FirstOrDefaultAsync(ct);
        }

        return new OrgUnitDto(raw.PublicId, parentPublicId, raw.Name, raw.NameEn,
            raw.Code, raw.Level, raw.IsActive, raw.TenantId, successorPublicId);
    }

    public Task InvalidateCacheAsync(Guid tenantId, CancellationToken ct = default)
        => cache.RemoveAsync(TreeKey(tenantId), ct);

    /// <param name="orgUnitId">Internal long Id (surrogate key) of the OrgUnit.</param>
    public Task<bool> HasActiveChildrenAsync(long orgUnitId, CancellationToken ct = default)
        => db.Query<OrgUnit>().AnyAsync(x => x.ParentId == orgUnitId && x.IsActive, ct);

    /// <summary>
    /// Dapper row type for recursive CTE — uses internal long Id/ParentId columns.
    /// </summary>
    private sealed class OrgUnitRow
    {
        public long Id { get; init; }
        public Guid PublicId { get; init; }
        public long? ParentId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string NameEn { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public int Level { get; init; }
        public bool IsActive { get; init; }
        public Guid TenantId { get; init; }
        public long? SuccessorId { get; init; }
    }
}
