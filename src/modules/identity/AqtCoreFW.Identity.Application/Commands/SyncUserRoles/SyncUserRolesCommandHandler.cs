using AqtCoreFW.Identity.Application.Commands.RevokeToken;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.SyncUserRoles;

/// <summary>
/// Replaces user roles: removes roles not in the provided list, adds missing ones. Roles are resolved
/// by RoleId within the target user's tenant scope (display names are unique per tenant, not globally),
/// then the user-role links are diffed and applied by RoleId — avoiding the ambiguous global name lookup.
/// </summary>
public sealed class SyncUserRolesCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ICacheService cache,
    IMediator mediator,
    UserManager<ApplicationUser> userManager) : IRequestHandler<SyncUserRolesCommand, Result>
{
    public async Task<Result> Handle(SyncUserRolesCommand cmd, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(cmd.UserId, ct);
        if (user is null)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found"));

        // Resolve every requested role name to a concrete RoleId within the user's tenant scope.
        var requestedRoleIds = new HashSet<Guid>();
        var requestedRolesById = new Dictionary<Guid, ApplicationRole>();
        foreach (var name in cmd.Roles)
        {
            var role = await roles.GetByNameInTenantScopeAsync(name, user.TenantId, ct);
            if (role is null)
                return Result.Fail(new NotFoundError($"Role '{name}' not found"));
            requestedRoleIds.Add(role.Id);
            requestedRolesById[role.Id] = role;
        }

        var currentRoleIds = (await users.GetRoleIdsAsync(user.Id, ct)).ToHashSet();
        var roleIdsToRemove = currentRoleIds.Except(requestedRoleIds).ToList();
        var roleIdsToAdd = requestedRoleIds.Except(currentRoleIds).ToList();

        // Privilege-escalation guard: granting a privileged role via a role sync requires the actor to
        // be SystemAdmin. Mirrors AssignRole — guards the role NAME (Admin/SystemAdmin) as well as
        // RoleType.System, since a seeder may leave those roles defaulted to Business. A delegated role
        // manager (IDENTITY.ROLE.MANAGE) must not elevate anyone (incl. self) into the system tier.
        if (roleIdsToAdd.Any(id =>
        {
            var r = requestedRolesById[id];
            return r.RoleType == RoleType.System || r.Name == Roles.SystemAdmin || r.Name == Roles.Admin;
        }))
        {
            var actor = await userManager.FindByIdAsync(cmd.UpdatedBy.ToString());
            if (actor is null || !await userManager.IsInRoleAsync(actor, Roles.SystemAdmin))
                return Result.Fail(new ForbiddenError("Chỉ SystemAdmin mới được gán vai trò hệ thống."));
        }

        await users.RemoveFromRolesAsync(user.Id, roleIdsToRemove, ct);
        foreach (var roleId in roleIdsToAdd)
            await users.AddToRoleAsync(user.Id, roleId, ct);

        // Invalidate role cache
        await cache.RemoveAsync($"user-roles:{cmd.UserId}");

        // Revoke all active tokens — forces re-login with updated claims (C-02 security fix)
        if (roleIdsToRemove.Count > 0 || roleIdsToAdd.Count > 0)
        {
            await mediator.Send(new RevokeTokenCommand(TokenId: null, UserId: cmd.UserId, ActorId: cmd.UserId), ct);
        }

        return Result.Ok();
    }
}
