using System.Text.Json;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Builds the authorized menu tree filtered by a user's effective permission codes.
///
/// Strategy:
///   1. Load all active menus with their permission requirements from DB.
///   2. Include a menu item if the user holds at least one required permission
///      OR if the item has no permission requirements (public item).
///   3. Build tree by resolving ParentId references.
///   4. Cache the full menu structure in Redis (key: menu-tree:{tenantId}, TTL 30 min).
///      Filtering per user happens in-process from the cached flat list.
/// </summary>
public sealed class MenuService : IMenuService
{
    private const int CacheTtlMinutes = 30;
    private const string CacheKeyPrefix = "menu-tree:";

    private readonly IdentityDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<MenuService> _logger;

    public MenuService(
        IdentityDbContext db,
        IConnectionMultiplexer redis,
        ITenantContext tenantContext,
        ILogger<MenuService> logger)
    {
        _db = db;
        _redis = redis;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MenuNode>> GetMenuTreeAsync(
        IReadOnlySet<string> userPermissionCodes,
        CancellationToken ct = default)
    {
        var flatMenus = await GetCachedFlatMenusAsync(ct);
        return BuildAndPruneTree(flatMenus, userPermissionCodes, parentId: null);
    }

    // --- Flat menu loading with Redis cache ---

    private async Task<IReadOnlyList<CachedMenuEntry>> GetCachedFlatMenusAsync(CancellationToken ct)
    {
        var cacheKey = $"{CacheKeyPrefix}{_tenantContext.TenantId}";

        try
        {
            var redisDb = _redis.GetDatabase();
            var cached = await redisDb.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                var result = JsonSerializer.Deserialize<List<CachedMenuEntry>>(cached.ToString(), JsonOpts);
                if (result is not null) return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable reading {Key}, falling back to DB", cacheKey);
        }

        var menus = await LoadFromDbAsync(ct);
        await TryCacheAsync(cacheKey, menus);
        return menus;
    }

    private async Task<List<CachedMenuEntry>> LoadFromDbAsync(CancellationToken ct)
    {
        return await _db.AppMenus
            .Where(m => m.IsActive
                && (m.TenantId == null || m.TenantId == _tenantContext.TenantId))
            .Include(m => m.RolePermissions)
            .OrderBy(m => m.SortOrder)
            .Select(m => new CachedMenuEntry(
                m.PublicId,
                m.ParentId,
                m.Code,
                m.Title,
                m.Description,
                m.Icon,
                m.Route,
                m.SortOrder,
                m.RolePermissions.Select(rp => rp.PermissionCode).ToList()))
            .ToListAsync(ct);
    }

    private async Task TryCacheAsync(string key, List<CachedMenuEntry> menus)
    {
        try
        {
            var redisDb = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(menus, JsonOpts);
            await redisDb.StringSetAsync(key, json, TimeSpan.FromMinutes(CacheTtlMinutes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable writing {Key}", key);
        }
    }

    // --- Filtering and tree building ---
    // F4 fix: Build tree from ALL menus, then prune branches where the user has no access.
    // Previous approach filtered the flat list first, orphaning children of unauthorized parents.

    /// <summary>
    /// Build full tree, then prune: remove leaf nodes the user can't access,
    /// then remove parent nodes with no remaining children (unless they have a route).
    /// This preserves authorized children even when their parent has a permission gate.
    /// </summary>
    internal static IReadOnlyList<MenuNode> BuildAndPruneTree(
        IReadOnlyList<CachedMenuEntry> menus,
        IReadOnlySet<string> userCodes,
        Guid? parentId)
    {
        var children = menus
            .Where(m => m.ParentId == parentId)
            .OrderBy(m => m.SortOrder)
            .ToList();

        var result = new List<MenuNode>();

        foreach (var m in children)
        {
            // Recursively build subtree first (returns only authorized descendants)
            var subtree = BuildAndPruneTree(menus, userCodes, m.Id);

            // Determine if this node has DB children at all (i.e. is a group/parent node)
            var hasDbChildren = menus.Any(x => x.ParentId == m.Id);

            var isAuthorized = m.PermissionCodes.Count == 0
                               || m.PermissionCodes.Any(userCodes.Contains);

            if (hasDbChildren)
            {
                // Group/parent node: keep only when it has at least one authorized descendant.
                // Never include an empty group header — that is the F10 fix.
                if (subtree.Count == 0) continue;

                result.Add(new MenuNode
                {
                    Id = m.Id, Code = m.Code, Title = m.Title,
                    Description = m.Description, Icon = m.Icon,
                    Route = m.Route, SortOrder = m.SortOrder,
                    Children = subtree
                });
            }
            else
            {
                // True leaf node: include only if the user is authorized
                if (!isAuthorized) continue;

                result.Add(new MenuNode
                {
                    Id = m.Id, Code = m.Code, Title = m.Title,
                    Description = m.Description, Icon = m.Icon,
                    Route = m.Route, SortOrder = m.SortOrder,
                    Children = []
                });
            }
        }

        return result;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>Internal flat representation cached in Redis.</summary>
internal sealed record CachedMenuEntry(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Title,
    string? Description,
    string? Icon,
    string? Route,
    int SortOrder,
    List<string> PermissionCodes);
