using AqtCoreFW.Identity.Application.Commands.SyncRolePermissions;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Commands.SyncRolePermissions;

/// <summary>
/// Replaces all permissions for a role (PUT sync — idempotent).
/// Lives in Infrastructure because it needs IdentityDbContext for RolePermissions.
/// </summary>
public sealed class SyncRolePermissionsCommandHandler(
    IRoleRepository roleRepository,
    IdentityDbContext db,
    IBulkRoleChangeService bulkRoleChangeService,
    UserManager<ApplicationUser> userManager,
    ISodConflictChecker sodChecker,
    IPublisher publisher,
    IGuidGenerator guidGen)
    : IRequestHandler<SyncRolePermissionsCommand, Result<SyncRolePermissionsResult>>
{
    public async Task<Result<SyncRolePermissionsResult>> Handle(
        SyncRolePermissionsCommand cmd, CancellationToken ct)
    {
        // 1. Load role
        var role = await roleRepository.GetByIdAsync(cmd.RoleId, ct);
        if (role is null)
            return Result.Fail<SyncRolePermissionsResult>(
                new NotFoundError("Vai trò không tồn tại."));

        // C-02: Tenant isolation
        if (role.TenantId is not null && role.TenantId != cmd.TenantId)
            return Result.Fail<SyncRolePermissionsResult>(
                new ForbiddenError("Không có quyền chỉnh sửa phân quyền vai trò này."));

        // H-02: Validate all permission IDs exist
        if (cmd.PermissionIds.Count > 0)
        {
            var validIds = await db.Permissions
                .Where(p => cmd.PermissionIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToHashSetAsync(ct);
            var invalidIds = cmd.PermissionIds.Except(validIds).ToList();
            if (invalidIds.Count > 0)
                return Result.Fail<SyncRolePermissionsResult>(
                    new ValidationError(
                        $"Quyền không tồn tại: {string.Join(", ", invalidIds)}"));
        }

        // RT-06: Sensitive permission guard — only SystemAdmin can assign sensitive perms
        if (cmd.PermissionIds.Count > 0)
        {
            var hasSensitive = await db.Permissions
                .AnyAsync(p => cmd.PermissionIds.Contains(p.Id) && p.IsSensitive, ct);

            if (hasSensitive)
            {
                var actor = await userManager.FindByIdAsync(cmd.ActorId.ToString());
                if (actor is null || !await userManager.IsInRoleAsync(actor, "SystemAdmin"))
                    return Result.Fail<SyncRolePermissionsResult>(
                        new ForbiddenError("Chỉ SystemAdmin mới được gán quyền nhạy cảm."));
            }
        }

        // 3. Diff: current vs requested
        var currentIds = await db.RolePermissions
            .Where(rp => rp.RoleId == cmd.RoleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct);

        var currentSet = currentIds.ToHashSet();
        var requestedSet = cmd.PermissionIds.ToHashSet();

        var toAdd = requestedSet.Except(currentSet).ToList();
        var toRemove = currentSet.Except(requestedSet).ToList();

        // H-01: SoD pre-check BEFORE save — return warnings without saving if not confirmed
        if (!cmd.Confirmed && toAdd.Count > 0)
        {
            var warnings = new List<string>();
            // Intentional: check ALL users; prior Take(10) silently truncated violations for roles >10 users
            var usersInRole = await db.UserRoles
                .Where(ur => ur.RoleId == cmd.RoleId)
                .Select(ur => ur.UserId)
                .ToListAsync(ct);

            foreach (var userId in usersInRole)
            {
                var violations = await sodChecker.CheckAsync(userId, cmd.RoleId, ct);
                foreach (var v in violations)
                    warnings.Add($"SoD: {v.PermissionCodeA} ↔ {v.PermissionCodeB} ({v.Severity}) - {v.Description}");
            }

            if (warnings.Count > 0)
                return Result.Ok(new SyncRolePermissionsResult
                {
                    Warnings = warnings.Distinct().ToList(),
                    RequiresConfirmation = true
                });
        }

        // 4. Apply diff
        if (toRemove.Count > 0)
        {
            var removeEntities = await db.RolePermissions
                .Where(rp => rp.RoleId == cmd.RoleId && toRemove.Contains(rp.PermissionId))
                .ToListAsync(ct);
            db.RolePermissions.RemoveRange(removeEntities);
        }

        if (toAdd.Count > 0)
        {
            db.RolePermissions.AddRange(toAdd.Select(pid => new RolePermission
            {
                RoleId = cmd.RoleId,
                PermissionId = pid
            }));
        }

        // 5. Inline audit write — only when Confirmed=true AND violations exist
        // Must be BEFORE SaveChangesAsync for atomic commit (permission diff + audit rows)
        if (cmd.Confirmed && toAdd.Count > 0)
        {
            var usersInRole = await db.UserRoles
                .Where(ur => ur.RoleId == cmd.RoleId)
                .Select(ur => ur.UserId)
                .ToListAsync(ct);

            foreach (var userId in usersInRole)
            {
                var violations = await sodChecker.CheckAsync(userId, cmd.RoleId, ct);
                if (violations.Count > 0)
                {
                    var logs = violations.Select(v => SodViolationLog.Create(
                        guidGen,
                        cmd.TenantId,
                        userId,
                        cmd.RoleId,
                        v.RuleId,
                        cmd.Justification,
                        cmd.ActorId));
                    db.SodViolationLogs.AddRange(logs);
                }
            }
        }

        if (toAdd.Count > 0 || toRemove.Count > 0)
            await db.SaveChangesAsync(ct);

        // RT-02 / RBAC-1: Invalidate caches
        if (toAdd.Count > 0 || toRemove.Count > 0)
        {
            // Global user-roles cache wipe (claims enrichment) — pre-existing defensive flush.
            await bulkRoleChangeService.InvalidateRoleCacheForAllUsersAsync();

            // Scoped perm:{userId} cache wipe — every user whose effective perms include this role
            // (direct + group + active delegate of a holder) must reload, otherwise stale codes
            // survive up to 20 min (Redis TTL 10 min + React Query staleTime 10 min). RBAC-1 v2.133p7.
            var affectedUserIds = await GetUsersHoldingRoleAsync(cmd.RoleId, ct);
            await bulkRoleChangeService.InvalidatePermissionCacheForUsersAsync(affectedUserIds);
        }

        // Raise domain event for audit
        if (toAdd.Count > 0 || toRemove.Count > 0)
        {
            await publisher.Publish(
                new RolePermissionsChangedEvent(cmd.RoleId, toAdd, toRemove, cmd.ActorId, guidGen.NewIdRandom()), ct);
        }

        return Result.Ok(new SyncRolePermissionsResult
        {
            Warnings = [],
            RequiresConfirmation = false
        });
    }

    /// <summary>
    /// Returns the union of user IDs whose effective permissions could change when this role's
    /// permission mapping changes. Three inheritance paths (mirrors <c>EffectivePermissionService.BuildFromDbAsync</c>):
    ///   1. Direct (AspNetUserRoles).
    ///   2. Group (UserGroupMembership ∩ GroupRoleAssignments).
    ///   3. Delegation (UserDelegations.DelegateId where Delegator holds the role via path 1 or 2 and delegation is active).
    /// Conservative over-invalidation: any active delegate of a holder is wiped regardless of <c>DelegatedRoleIds</c>
    /// scope filter — admin-edit path is low-frequency, cost-of-overinvalidation = one extra DB miss on next read.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> GetUsersHoldingRoleAsync(Guid roleId, CancellationToken ct)
    {
        var directUsers = await db.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);

        var groupUsers = await db.GroupRoleAssignments
            .Where(gra => gra.RoleId == roleId)
            .Join(db.UserGroupMemberships,
                  gra => gra.GroupId,
                  m => m.GroupId,
                  (_, m) => m.UserId)
            .ToListAsync(ct);

        var directAndGroup = directUsers.Concat(groupUsers).Distinct().ToList();

        if (directAndGroup.Count == 0)
            return directAndGroup;

        var now = DateTime.UtcNow;
        var delegateUsers = await db.UserDelegations
            .Where(d => directAndGroup.Contains(d.DelegatorId)
                        && !d.IsRevoked
                        && d.ValidFrom <= now
                        && d.ValidTo >= now)
            .Select(d => d.DelegateId)
            .ToListAsync(ct);

        return directAndGroup.Concat(delegateUsers).Distinct().ToList();
    }
}
