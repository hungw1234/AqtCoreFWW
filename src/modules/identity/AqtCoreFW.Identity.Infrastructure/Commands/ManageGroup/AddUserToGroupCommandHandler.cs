using AqtCoreFW.Identity.Application.Commands.ManageGroup;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Commands.ManageGroup;

/// <summary>
/// Adds a user to a group. Idempotent — returns Ok if membership already exists.
/// GroupId in command is PublicId (Guid T2 AK). Junction FK uses long group.Id.
/// Invalidates effective-permission cache so group roles apply immediately.
/// </summary>
public sealed class AddUserToGroupCommandHandler(
    IdentityDbContext db,
    IEffectivePermissionService permissionService)
    : IRequestHandler<AddUserToGroupCommand, Result>
{
    public async Task<Result> Handle(AddUserToGroupCommand cmd, CancellationToken ct)
    {
        // Resolve group by PublicId (T2 external key)
        var group = await db.UserGroups.FirstOrDefaultAsync(g => g.PublicId == cmd.GroupId, ct);
        if (group is null)
            return Result.Fail(new NotFoundError($"Group {cmd.GroupId} not found."));

        var userExists = await db.Users.AnyAsync(u => u.Id == cmd.UserId, ct);
        if (!userExists)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found."));

        // Idempotent — skip if already a member (junction FK is long group.Id)
        var alreadyMember = await db.UserGroupMemberships
            .AnyAsync(m => m.GroupId == group.Id && m.UserId == cmd.UserId, ct);

        if (!alreadyMember)
        {
            db.UserGroupMemberships.Add(new UserGroupMembership
            {
                GroupId = group.Id,
                UserId = cmd.UserId,
                AddedBy = cmd.AddedBy,
                AddedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        // Invalidate cache so group-inherited roles take effect immediately
        await permissionService.InvalidateAsync(cmd.UserId);

        return Result.Ok();
    }
}
