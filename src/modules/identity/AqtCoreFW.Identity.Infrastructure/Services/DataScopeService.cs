using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Resolves effective data scope for a user by merging:
///   1. Role scopes from direct role assignments (AspNetUserRoles)
///   2. Role scopes from group memberships (UserGroupMembership → GroupRoleAssignment)
///   3. User-level overrides (UserDataScopeOverride, not expired)
///
/// Resolution rules (first match wins on IsAll):
///   ALL        → short-circuit, return AllAccess
///   SELF       → set IncludeSelf
///   ASSIGNED   → set IncludeAssigned
///   ORG_UNIT   → add root OrgUnitId
///   ORG_TREE   → recursively expand subtree via IOrgUnitModuleClient (tenant-scoped)
///   BY_FIELD   → add FieldFilter(ScopeField, ScopeValue)
///   CUSTOM_LIST→ treated as BY_FIELD (ScopeField=null → ignored)
///
/// Cache: Redis key "scope:{userId}", TTL 5 min. Call InvalidateAsync after role/override changes.
/// </summary>
public sealed class DataScopeService : IDataScopeService
{
    private const int CacheTtlSeconds = 300;
    private const string CacheKeyPrefix = "scope:";

    private readonly IdentityDbContext _db;
    private readonly IOrgUnitModuleClient _orgUnits;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DataScopeService> _logger;

    public DataScopeService(
        IdentityDbContext db,
        IOrgUnitModuleClient orgUnits,
        IConnectionMultiplexer redis,
        ILogger<DataScopeService> logger)
    {
        _db = db;
        _orgUnits = orgUnits;
        _redis = redis;
        _logger = logger;
    }

    public async Task<ResolvedDataScope> ResolveAsync(Guid userId, CancellationToken ct = default)
    {
        // 1. Try Redis cache
        var cached = await TryGetCachedAsync(userId);
        if (cached is not null) return cached;

        // 2. Build from DB
        var resolved = await BuildFromDbAsync(userId, ct);

        // 3. Write back to cache
        await TrySetCachedAsync(userId, resolved);

        return resolved;
    }

    public async Task InvalidateAsync(Guid userId)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync($"{CacheKeyPrefix}{userId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable invalidating scope:{UserId}", userId);
        }
    }

    // --- DB resolution ---

    private async Task<ResolvedDataScope> BuildFromDbAsync(Guid userId, CancellationToken ct)
    {
        // Lookup user TenantId once — used for ORG_TREE subtree expansion (defense-in-depth)
        var tenantId = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.TenantId)
            .FirstOrDefaultAsync(ct);

        // Collect all role IDs: direct + via group membership
        var directRoleIds = await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var groupRoleIds = await _db.UserGroupMemberships
            .Where(m => m.UserId == userId)
            .Join(_db.GroupRoleAssignments,
                  m => m.GroupId,
                  gra => gra.GroupId,
                  (_, gra) => gra.RoleId)
            .Distinct()
            .ToListAsync(ct);

        var allRoleIds = directRoleIds.Union(groupRoleIds).Distinct().ToList();

        // Load RoleDataScopes with scope type codes
        var roleScopes = await _db.RoleDataScopes
            .Include(rds => rds.DataScopeType)
            .Where(rds => allRoleIds.Contains(rds.RoleId))
            .ToListAsync(ct);

        // Load active user-level overrides
        var now = DateTime.UtcNow;
        var userOverrides = await _db.UserDataScopeOverrides
            .Include(o => o.DataScopeType)
            .Where(o => o.UserId == userId
                        && (o.ExpiresAtUtc == null || o.ExpiresAtUtc > now))
            .ToListAsync(ct);

        // Merge all scope entries (role scopes + overrides) into unified result
        return await MergeScopesAsync(userId, tenantId, roleScopes, userOverrides, ct);
    }

    private async Task<ResolvedDataScope> MergeScopesAsync(
        Guid userId,
        Guid? tenantId,
        IEnumerable<Domain.Entities.RoleDataScope> roleScopes,
        IEnumerable<Domain.Entities.UserDataScopeOverride> userOverrides,
        CancellationToken ct)
    {
        var includeSelf = false;
        var includeAssigned = false;
        var orgUnitIds = new List<Guid>();
        var fieldFilters = new List<FieldFilter>();

        // Helper: process a single scope code + optional field info
        async Task ProcessScopeAsync(string code, string? scopeField, string? scopeValue, Guid? orgUnitId)
        {
            switch (code)
            {
                case "ALL":
                    return; // handled below via short-circuit check
                case "SELF":
                    includeSelf = true;
                    break;
                case "ASSIGNED":
                    includeAssigned = true;
                    break;
                case "ORG_UNIT":
                    if (orgUnitId.HasValue)
                        orgUnitIds.Add(orgUnitId.Value);
                    break;
                case "ORG_TREE":
                    if (orgUnitId.HasValue)
                    {
                        var descendants = await ExpandOrgTreeAsync(orgUnitId.Value, tenantId, ct);
                        orgUnitIds.AddRange(descendants);
                    }
                    break;
                case "BY_FIELD":
                case "CUSTOM_LIST":
                    if (!string.IsNullOrWhiteSpace(scopeField) && !string.IsNullOrWhiteSpace(scopeValue))
                        fieldFilters.Add(new FieldFilter(scopeField, scopeValue));
                    break;
            }
        }

        // Short-circuit: if ANY scope is ALL, return immediately
        var allCodes = roleScopes.Select(rs => rs.DataScopeType.Code)
            .Concat(userOverrides.Select(o => o.DataScopeType.Code));
        if (allCodes.Any(c => c == "ALL"))
            return ResolvedDataScope.AllAccess();

        // Process role scopes — ScopeField/ScopeValue carry BY_FIELD data;
        // ORG_UNIT/ORG_TREE use ScopeValue as the org unit Guid
        foreach (var rs in roleScopes)
        {
            Guid? orgId = null;
            if (rs.DataScopeType.Code is "ORG_UNIT" or "ORG_TREE"
                && rs.ScopeValue is not null
                && Guid.TryParse(rs.ScopeValue, out var gid))
                orgId = gid;

            await ProcessScopeAsync(rs.DataScopeType.Code, rs.ScopeField, rs.ScopeValue, orgId);
        }

        foreach (var ov in userOverrides)
        {
            Guid? orgId = null;
            if (ov.DataScopeType.Code is "ORG_UNIT" or "ORG_TREE"
                && ov.ScopeValue is not null
                && Guid.TryParse(ov.ScopeValue, out var gid))
                orgId = gid;

            await ProcessScopeAsync(ov.DataScopeType.Code, ov.ScopeField, ov.ScopeValue, orgId);
        }

        // If nothing resolved, return NoAccess
        if (!includeSelf && !includeAssigned && orgUnitIds.Count == 0 && fieldFilters.Count == 0)
            return ResolvedDataScope.NoAccess();

        return new ResolvedDataScope
        {
            IncludeSelf = includeSelf,
            IncludeAssigned = includeAssigned,
            OrgUnitIds = orgUnitIds.Distinct().ToList(),
            FieldFilters = fieldFilters
        };
    }

    private async Task<IEnumerable<Guid>> ExpandOrgTreeAsync(Guid rootId, Guid? tenantId, CancellationToken ct)
    {
        // Defense-in-depth: ORG_TREE expansion requires user's tenant context.
        // If user has no tenant (e.g. orphaned record), fail safe to root-only.
        if (!tenantId.HasValue)
        {
            _logger.LogWarning("OrgTree expansion skipped — user has no TenantId. Root {RootId}", rootId);
            return [rootId];
        }

        try
        {
            return await _orgUnits.ExpandSubtreeAsync(rootId, tenantId.Value, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OrgTree expansion failed for root {RootId}, returning root only", rootId);
            return [rootId];
        }
    }

    // --- Redis helpers ---

    private async Task<ResolvedDataScope?> TryGetCachedAsync(Guid userId)
    {
        try
        {
            var redisDb = _redis.GetDatabase();
            var cached = await redisDb.StringGetAsync($"{CacheKeyPrefix}{userId}");
            if (cached.HasValue)
                return JsonSerializer.Deserialize<ResolvedDataScope>(cached.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable reading scope:{UserId}, falling back to DB", userId);
        }
        return null;
    }

    private async Task TrySetCachedAsync(Guid userId, ResolvedDataScope scope)
    {
        try
        {
            var redisDb = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(scope);
            await redisDb.StringSetAsync(
                $"{CacheKeyPrefix}{userId}",
                json,
                TimeSpan.FromSeconds(CacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable writing scope:{UserId}", userId);
        }
    }
}
