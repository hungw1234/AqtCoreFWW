using AqtCoreFW.Identity.Application.Commands.RevokeToken;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.AssignRole;

public sealed class AssignRoleCommandHandler : IRequestHandler<AssignRoleCommand, Result>
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ICacheService _cache;
    private readonly IMediator _mediator;
    private readonly UserManager<ApplicationUser> _userManager;

    public AssignRoleCommandHandler(
        IUserRepository users,
        IRoleRepository roles,
        ICacheService cache,
        IMediator mediator,
        UserManager<ApplicationUser> userManager)
    {
        _users = users;
        _roles = roles;
        _cache = cache;
        _mediator = mediator;
        _userManager = userManager;
    }

    public async Task<Result> Handle(AssignRoleCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(cmd.UserId, ct);
        if (user is null)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found"));

        // Resolve the role within the target user's tenant scope — role display names are unique
        // per tenant, so a global name lookup is ambiguous once two tenants share a business-role
        // name. System-wide (null-tenant) roles still resolve via the scope's null fallback.
        var role = await _roles.GetByNameInTenantScopeAsync(cmd.RoleName, user.TenantId, ct);
        if (role is null)
            return Result.Fail(new NotFoundError($"Role '{cmd.RoleName}' not found"));

        // Privilege-escalation guard: a privileged role may only be granted by a SystemAdmin actor.
        // The elevation comes from the role NAME (IsInRole("SystemAdmin")/"Admin" bypasses are checked
        // across the codebase), so we guard the name as well as RoleType.System — a seeder that creates
        // Admin/SystemAdmin without setting RoleType leaves them defaulted to Business, which would
        // otherwise make a RoleType-only guard inert. Business (tenant) roles stay grantable by the
        // delegated IDENTITY.ROLE.MANAGE manager.
        var isPrivilegedRole = role.RoleType == RoleType.System
            || role.Name == Roles.SystemAdmin
            || role.Name == Roles.Admin;
        if (isPrivilegedRole)
        {
            var actor = await _userManager.FindByIdAsync(cmd.AssignedBy.ToString());
            if (actor is null || !await _userManager.IsInRoleAsync(actor, Roles.SystemAdmin))
                return Result.Fail(new ForbiddenError("Chỉ SystemAdmin mới được gán vai trò hệ thống."));
        }

        await _users.AddToRoleAsync(user.Id, role.Id, ct);

        // Invalidate role cache so ClaimsEnrichmentTransformer picks up change immediately
        await _cache.RemoveAsync($"user-roles:{cmd.UserId}");

        // Revoke all active tokens — forces re-login with updated claims (C-02 security fix)
        await _mediator.Send(new RevokeTokenCommand(TokenId: null, UserId: cmd.UserId, ActorId: cmd.UserId), ct);

        return Result.Ok();
    }
}
