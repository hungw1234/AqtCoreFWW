using System.Security.Claims;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Enriches ClaimsPrincipal with role claims and active delegation claims after authentication.
/// Uses Redis L1 cache (TTL 5min) to avoid DB round-trips on every request.
/// Cache keys: user-roles:{userId} | delegation:{userId} | clearance:{userId}
///
/// Security:
///   C6/F-16 — clearance_level is re-injected from DB/cache to override potentially stale JWT value.
///   C5/F-15 — delegated_clearance_level is capped to Min(delegate, delegator) clearance.
/// </summary>
public sealed class ClaimsEnrichmentTransformer : IClaimsTransformation
{
    private const int CacheTtlSeconds = 300; // 5 min

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IdentityDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ClaimsEnrichmentTransformer> _logger;

    public ClaimsEnrichmentTransformer(
        UserManager<ApplicationUser> userManager,
        IdentityDbContext dbContext,
        IConnectionMultiplexer redis,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ClaimsEnrichmentTransformer> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _redis = redis;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Request-scoped cancellation token. <see cref="IClaimsTransformation.TransformAsync"/> exposes
    /// no <see cref="CancellationToken"/>, so the hot-path EF queries below honour the in-flight
    /// request's abort signal via <see cref="HttpContext.RequestAborted"/> (falls back to None when
    /// invoked outside an HTTP request, e.g. background re-auth).
    /// </summary>
    private CancellationToken RequestAborted =>
        _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var userIdStr = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? principal.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return principal;

        // Avoid double-enrichment if roles already present
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role))
            return principal;

        var identity = (ClaimsIdentity)principal.Identity!;

        await AddRoleClaimsAsync(identity, userId);

        // C6/F-16: Re-inject clearance_level from DB/cache to override stale JWT claim
        await RefreshClearanceLevelClaimAsync(identity, userId);

        await AddDelegationClaimsAsync(identity, userId);

        // Add org/employee/auth-source claims from user entity
        await AddProfileClaimsAsync(identity, userId);

        return principal;
    }

    // --- Role claims ---

    private async Task AddRoleClaimsAsync(ClaimsIdentity identity, Guid userId)
    {
        var roles = await GetRolesFromCacheOrDbAsync(userId);
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
    }

    // --- C6/F-16: Clearance level refresh ---

    /// <summary>
    /// Replaces the clearance_level claim with the current DB/cache value.
    /// Ensures admin-initiated clearance downgrades take effect immediately
    /// without waiting for token expiry.
    /// </summary>
    private async Task RefreshClearanceLevelClaimAsync(ClaimsIdentity identity, Guid userId)
    {
        var currentClearance = await GetClearanceLevelFromCacheOrDbAsync(userId);

        // Remove stale JWT claim if present, then re-add from authoritative source
        var existing = identity.FindFirst("clearance_level");
        if (existing is not null)
            identity.RemoveClaim(existing);

        identity.AddClaim(new Claim("clearance_level", currentClearance.ToString()));
    }

    private async Task<IReadOnlyList<string>> GetRolesFromCacheOrDbAsync(Guid userId)
    {
        var cacheKey = $"user-roles:{userId}";
        try
        {
            var db = _redis.GetDatabase();
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue)
                return System.Text.Json.JsonSerializer
                    .Deserialize<List<string>>(cached.ToString()) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable reading user-roles:{UserId}, falling back to DB", userId);
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return [];

        var roles = (await _userManager.GetRolesAsync(user)).ToList();

        try
        {
            var db = _redis.GetDatabase();
            var json = System.Text.Json.JsonSerializer.Serialize(roles);
            await db.StringSetAsync(cacheKey, json, TimeSpan.FromSeconds(CacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable writing user-roles:{UserId}", userId);
        }

        return roles;
    }

    // --- Delegation claims (inject delegator's roles, cap clearance) ---

    /// <summary>
    /// Injects delegated role claims for every active delegation where this user is the delegate.
    ///
    /// Phase E enhancement:
    ///   - If delegation.DelegatedRoleIds is set, only inject those specific roles (scoped delegation).
    ///   - If null, inject all delegator roles (backward-compat with pre-Phase-E delegations).
    ///   - Always adds "delegation_id" claim for audit traceability.
    ///
    /// C5/F-15: delegated_clearance_level is capped to Min(delegate, delegator) clearance.
    /// </summary>
    private async Task AddDelegationClaimsAsync(ClaimsIdentity identity, Guid userId)
    {
        var activeDelegations = await GetActiveDelegationsAsync(userId);
        if (activeDelegations.Count == 0) return;

        // Delegate's own clearance (already refreshed above — read back from identity)
        var delegateClearanceClaim = identity.FindFirst("clearance_level")?.Value;
        Enum.TryParse<ClassificationLevel>(delegateClearanceClaim, out var delegateClearance);

        foreach (var (delegationId, delegatorId, scopedRoleIds) in activeDelegations)
        {
            // Audit traceability — consumers can distinguish which delegation granted the roles
            identity.AddClaim(new Claim("delegation_id", delegationId.ToString()));

            IReadOnlyList<string> rolesToInject;

            if (scopedRoleIds is { Count: > 0 })
            {
                // Scoped delegation: load only the roles matching the allowed role IDs
                rolesToInject = await GetScopedRolesAsync(delegatorId, scopedRoleIds);
            }
            else
            {
                // Null/empty DelegatedRoleIds → backward-compat: inject all delegator roles
                rolesToInject = await GetRolesFromCacheOrDbAsync(delegatorId);
            }

            foreach (var role in rolesToInject)
            {
                identity.AddClaim(new Claim("delegated_role", role));
                identity.AddClaim(new Claim("delegated_from", delegatorId.ToString()));
            }

            // C5/F-15: Cap effective clearance to the lower of delegate and delegator levels
            var delegatorClearance = await GetClearanceLevelFromCacheOrDbAsync(delegatorId);
            var effectiveClearance = (ClassificationLevel)Math.Min(
                (int)delegateClearance, (int)delegatorClearance);
            identity.AddClaim(new Claim("delegated_clearance_level", effectiveClearance.ToString()));
        }
    }

    /// <summary>
    /// Returns role names for the delegator that are restricted to the given role ID allow-list.
    /// Used for scoped (partial) delegation where DelegatedRoleIds is set.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetScopedRolesAsync(
        Guid delegatorId, IReadOnlyList<Guid> allowedRoleIds)
    {
        try
        {
            return await _dbContext.UserRoles
                .Where(ur => ur.UserId == delegatorId && allowedRoleIds.Contains(ur.RoleId))
                .Join(_dbContext.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
                .Where(name => name != null)
                .ToListAsync(RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed loading scoped roles for delegator:{DelegatorId}", delegatorId);
            return [];
        }
    }

    /// <summary>
    /// Returns active delegations for the given delegate user as (delegationId, delegatorId, scopedRoleIds) tuples.
    /// Uses Redis cache (key: delegation:{userId}) storing delegator IDs for backward compat;
    /// falls back to DB and always fetches DelegatedRoleIds from DB (not cached — small payload).
    /// </summary>
    private async Task<IReadOnlyList<(Guid DelegationId, Guid DelegatorId, IReadOnlyList<Guid>? ScopedRoleIds)>>
        GetActiveDelegationsAsync(Guid delegateUserId)
    {
        var now = DateTime.UtcNow;

        // Always load from DB to get DelegatedRoleIds — cached delegator ID list used only as
        // a fast-path existence check; full delegation rows needed for scoped role support.
        try
        {
            var rows = await _dbContext.UserDelegations
                .Where(d => d.DelegateId == delegateUserId
                            && d.Status == AqtCoreFW.Identity.Domain.Entities.DelegationStatus.Active
                            && !d.IsRevoked
                            && d.ValidFrom <= now
                            && d.ValidTo >= now)
                .Select(d => new { d.PublicId, d.DelegatorId, d.DelegatedRoleIds })
                .ToListAsync(RequestAborted);

            // Refresh delegator ID list cache for ClaimsEnrichment TTL window
            var delegatorIds = rows.Select(r => r.DelegatorId).ToList();
            await TryCacheDelegatorIdsAsync(delegateUserId, delegatorIds);

            return rows
                .Select(r => (r.PublicId, r.DelegatorId, (IReadOnlyList<Guid>?)ParseRoleIds(r.DelegatedRoleIds)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DB unavailable loading delegations for:{UserId}, falling back to cache", delegateUserId);
        }

        // Fallback: use cached delegator IDs without scope (no DelegatedRoleIds available)
        var cachedIds = await GetCachedDelegatorIdsAsync(delegateUserId);
        return cachedIds
            .Select(id => (Guid.Empty, id, (IReadOnlyList<Guid>?)null))
            .ToList();
    }

    private static IReadOnlyList<Guid>? ParseRoleIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json); }
        catch { return null; }
    }

    private async Task<IReadOnlyList<Guid>> GetCachedDelegatorIdsAsync(Guid delegateUserId)
    {
        var cacheKey = $"delegation:{delegateUserId}";
        try
        {
            var db = _redis.GetDatabase();
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue)
                return System.Text.Json.JsonSerializer
                    .Deserialize<List<Guid>>(cached.ToString()) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable reading delegation:{UserId}", delegateUserId);
        }
        return [];
    }

    private async Task TryCacheDelegatorIdsAsync(Guid delegateUserId, List<Guid> delegatorIds)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = System.Text.Json.JsonSerializer.Serialize(delegatorIds);
            await db.StringSetAsync($"delegation:{delegateUserId}", json, TimeSpan.FromSeconds(CacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable writing delegation:{UserId}", delegateUserId);
        }
    }

    // --- Profile claims (org_unit_id, employee_code, auth_source) ---

    /// <summary>
    /// Adds stable profile claims sourced directly from ApplicationUser fields.
    /// These are not cached separately — the user entity is already loaded by
    /// GetClearanceLevelFromCacheOrDbAsync or UserManager in most flows.
    /// Claims are skipped if they already exist to avoid duplicate injection.
    /// </summary>
    private async Task AddProfileClaimsAsync(ClaimsIdentity identity, Guid userId)
    {
        // Skip if profile claims already present (re-entrant guard)
        if (identity.HasClaim(c => c.Type == "employee_code"))
            return;

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return;

        if (user.PrimaryOrgUnitId.HasValue)
            identity.AddClaim(new Claim("org_unit_id", user.PrimaryOrgUnitId.Value.ToString()));

        if (!string.IsNullOrWhiteSpace(user.EmployeeCode))
            identity.AddClaim(new Claim("employee_code", user.EmployeeCode));

        // auth_source: LOCAL | AD | VNEID | LDAP — always add for downstream consumers
        identity.AddClaim(new Claim("auth_source", user.AuthSource));
    }

    // --- Clearance level cache/DB helper ---

    /// <summary>
    /// Loads ClearanceLevel from Redis cache (key: clearance:{userId}), falling back to DB.
    /// Returns ClassificationLevel.Public on failure so access is denied rather than granted.
    /// </summary>
    /// <remarks>
    /// H-04 IMPORTANT: When implementing UpdateClearanceLevelCommand, you MUST invalidate
    /// the Redis cache key "clearance:{userId}" after updating the DB.
    /// Failure to do so creates a stale window where the user retains their old clearance
    /// level — unacceptable for classified GOV data (QĐ742).
    /// See: code-review-260319-1758-security-auth.md, finding H-04.
    /// </remarks>
    private async Task<ClassificationLevel> GetClearanceLevelFromCacheOrDbAsync(Guid userId)
    {
        var cacheKey = $"clearance:{userId}";
        try
        {
            var db = _redis.GetDatabase();
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue && Enum.TryParse<ClassificationLevel>(cached.ToString(), out var cachedLevel))
                return cachedLevel;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable reading clearance:{UserId}, falling back to DB", userId);
        }

        // Fall back to DB — fail closed (Public = lowest) on missing user
        var user = await _userManager.FindByIdAsync(userId.ToString());
        var level = user?.ClearanceLevel ?? ClassificationLevel.Public;

        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(cacheKey, level.ToString(), TimeSpan.FromSeconds(CacheTtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable writing clearance:{UserId}", userId);
        }

        return level;
    }
}
