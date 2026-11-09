using AqtCoreFW.Identity.Application.Commands.ManageGroup;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Commands.ManageGroup;

/// <summary>
/// Assigns a role to a group. Idempotent — returns Ok if assignment already exists.
/// GroupId in command is PublicId (Guid T2 AK). Junction FK uses long group.Id.
/// Invalidates effective-permission cache for all current members of the group.
/// </summary>
public sealed class AssignRoleToGroupCommandHandler(
    IdentityDbContext db,
    IEffectivePermissionService permissionService,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<AssignRoleToGroupCommand, Result>
{
    public async Task<Result> Handle(AssignRoleToGroupCommand cmd, CancellationToken ct)
    {
        // Resolve group by PublicId (T2 external key)
        var group = await db.UserGroups.FirstOrDefaultAsync(g => g.PublicId == cmd.GroupId, ct);
        if (group is null)
            return Result.Fail(new NotFoundError($"Group {cmd.GroupId} not found."));

        // ApplicationRole.Id is Guid (framework-locked IdentityRole<Guid>)
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == cmd.RoleId, ct);
        if (role is null)
            return Result.Fail(new NotFoundError($"Role {cmd.RoleId} not found."));

        // Privilege-escalation guard: assigning a role to a group makes every member inherit it, so a
        // privileged role may only be assigned by a SystemAdmin actor — otherwise a Group.Manage holder
        // could grant the group (and themselves, if a member) the SystemAdmin tier. Mirrors AssignRole:
        // guards the role NAME (Admin/SystemAdmin) as well as RoleType.System, since a seeder may leave
        // those roles defaulted to Business.
        var isPrivilegedRole = role.RoleType == RoleType.System
            || role.Name == Roles.SystemAdmin
            || role.Name == Roles.Admin;
        if (isPrivilegedRole)
        {
            var actor = await userManager.FindByIdAsync(cmd.AssignedBy.ToString());
            if (actor is null || !await userManager.IsInRoleAsync(actor, Roles.SystemAdmin))
                return Result.Fail(new ForbiddenError("Chỉ SystemAdmin mới được gán vai trò hệ thống cho nhóm."));
        }

        // Idempotent — skip if already assigned (junction FK uses long group.Id)
        var alreadyAssigned = await db.GroupRoleAssignments
            .AnyAsync(a => a.GroupId == group.Id && a.RoleId == cmd.RoleId, ct);

        if (!alreadyAssigned)
        {
            db.GroupRoleAssignments.Add(new GroupRoleAssignment
            {
                GroupId = group.Id,
                RoleId = cmd.RoleId,
                AssignedBy = cmd.AssignedBy,
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        // Invalidate cache for all members so the new role takes effect immediately
        var memberIds = await db.UserGroupMemberships
            .Where(m => m.GroupId == group.Id)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        foreach (var userId in memberIds)
            await permissionService.InvalidateAsync(userId);

        return Result.Ok();
    }
}
