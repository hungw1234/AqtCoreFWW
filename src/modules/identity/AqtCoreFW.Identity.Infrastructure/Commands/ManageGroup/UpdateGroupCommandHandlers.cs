using AqtCoreFW.Identity.Application.Commands.ManageGroup;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Commands.ManageGroup;

/// <summary>Updates group name, description, and active status via domain methods.</summary>
public sealed class UpdateGroupCommandHandler(IdentityDbContext db)
    : IRequestHandler<UpdateGroupCommand, Result>
{
    public async Task<Result> Handle(UpdateGroupCommand cmd, CancellationToken ct)
    {
        var group = await db.UserGroups.FirstOrDefaultAsync(g => g.PublicId == cmd.GroupId, ct);
        if (group is null)
            return Result.Fail(new NotFoundError($"Group {cmd.GroupId} not found."));

        // Update name/description via domain method (actorId not available on command — system)
        group.Update(cmd.Name, cmd.Description, Guid.Empty);

        if (cmd.IsActive.HasValue)
        {
            if (cmd.IsActive.Value) group.Activate(Guid.Empty);
            else group.Deactivate(Guid.Empty);
        }

        await db.SaveChangesAsync(ct);
        return Result.Ok();
    }
}

/// <summary>Removes a user from a group, then invalidates their permission cache.</summary>
public sealed class RemoveUserFromGroupCommandHandler(
    IdentityDbContext db,
    IEffectivePermissionService permissionService)
    : IRequestHandler<RemoveUserFromGroupCommand, Result>
{
    public async Task<Result> Handle(RemoveUserFromGroupCommand cmd, CancellationToken ct)
    {
        // Resolve group by PublicId to get long Id for junction lookup
        var group = await db.UserGroups.FirstOrDefaultAsync(g => g.PublicId == cmd.GroupId, ct);
        if (group is null)
            return Result.Fail(new NotFoundError($"Group {cmd.GroupId} not found."));

        var membership = await db.UserGroupMemberships
            .FirstOrDefaultAsync(m => m.GroupId == group.Id && m.UserId == cmd.UserId, ct);

        if (membership is null)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} is not a member of group {cmd.GroupId}."));

        db.UserGroupMemberships.Remove(membership);
        await db.SaveChangesAsync(ct);

        await permissionService.InvalidateAsync(cmd.UserId);
        return Result.Ok();
    }
}

/// <summary>Removes a role from a group, then invalidates cache for all affected members.</summary>
public sealed class RemoveRoleFromGroupCommandHandler(
    IdentityDbContext db,
    IEffectivePermissionService permissionService)
    : IRequestHandler<RemoveRoleFromGroupCommand, Result>
{
    public async Task<Result> Handle(RemoveRoleFromGroupCommand cmd, CancellationToken ct)
    {
        // Resolve group by PublicId to get long Id for junction lookup
        var group = await db.UserGroups.FirstOrDefaultAsync(g => g.PublicId == cmd.GroupId, ct);
        if (group is null)
            return Result.Fail(new NotFoundError($"Group {cmd.GroupId} not found."));

        var assignment = await db.GroupRoleAssignments
            .FirstOrDefaultAsync(a => a.GroupId == group.Id && a.RoleId == cmd.RoleId, ct);

        if (assignment is null)
            return Result.Fail(new NotFoundError($"Role {cmd.RoleId} is not assigned to group {cmd.GroupId}."));

        db.GroupRoleAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);

        // Invalidate cache for all current members so the removed role stops applying
        var memberIds = await db.UserGroupMemberships
            .Where(m => m.GroupId == group.Id)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        foreach (var userId in memberIds)
            await permissionService.InvalidateAsync(userId);

        return Result.Ok();
    }
}
