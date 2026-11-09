using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Checks Segregation of Duties violations by comparing a user's current
/// effective permissions against the permissions carried by the role being assigned.
///
/// Algorithm:
///   1. Load user's current permission codes via IEffectivePermissionService.
///   2. Load permission codes attached to the candidate role.
///   3. Find active SodConflictRules where one code is in user-set and the other
///      is in the candidate-role-set (bidirectional check).
/// </summary>
public sealed class SodConflictChecker : ISodConflictChecker
{
    private readonly IdentityDbContext _db;
    private readonly IEffectivePermissionService _permissionService;
    private readonly ITenantContext _tenantContext;

    public SodConflictChecker(
        IdentityDbContext db,
        IEffectivePermissionService permissionService,
        ITenantContext tenantContext)
    {
        _db = db;
        _permissionService = permissionService;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<SodViolation>> CheckAsync(
        Guid userId,
        Guid roleToAssign,
        CancellationToken ct = default)
    {
        // 1. User's existing permission codes
        var summary = await _permissionService.GetSummaryAsync(userId, ct);
        var userCodes = summary.PermissionCodes;

        // 2. Permission codes on the role being assigned
        var roleCodes = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleToAssign)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToListAsync(ct);

        if (roleCodes.Count == 0 || userCodes.Count == 0)
            return [];

        // 3. Load active SoD rules that touch any code from either set
        //    Inline tenant filter — Identity module has NO global query filter
        var allCodes = userCodes.Union(roleCodes).ToList();

        var activeRules = await _db.SodConflictRules
            .Where(r => r.TenantId == _tenantContext.TenantId)
            .Where(r => r.IsActive
                && allCodes.Contains(r.PermissionCodeA)
                && allCodes.Contains(r.PermissionCodeB))
            .ToListAsync(ct);

        // 4. A conflict fires when one code is in user-set and the other in role-set
        var violations = new List<SodViolation>();

        foreach (var rule in activeRules)
        {
            bool aInUser = userCodes.Contains(rule.PermissionCodeA);
            bool bInUser = userCodes.Contains(rule.PermissionCodeB);
            bool aInRole = roleCodes.Contains(rule.PermissionCodeA);
            bool bInRole = roleCodes.Contains(rule.PermissionCodeB);

            bool conflict = (aInUser && bInRole) || (bInUser && aInRole);
            if (conflict)
            {
                violations.Add(new SodViolation(
                    rule.Id,
                    rule.PermissionCodeA,
                    rule.PermissionCodeB,
                    rule.Severity.ToString(),
                    rule.Description));
            }
        }

        return violations;
    }
}
