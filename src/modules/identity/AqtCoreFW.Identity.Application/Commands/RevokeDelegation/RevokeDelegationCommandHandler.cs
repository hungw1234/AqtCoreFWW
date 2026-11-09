using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.RevokeDelegation;

/// <summary>
/// Revokes a delegation. Authorization: the delegator (whose authority was lent) or an
/// Admin/SystemAdmin override — an arbitrary authenticated caller may not revoke a delegation
/// they neither own nor administer.
/// </summary>
public sealed class RevokeDelegationCommandHandler : IRequestHandler<RevokeDelegationCommand, Result>
{
    private readonly IDelegationRepository _delegations;
    private readonly ICacheService _cache;
    private readonly IGuidGenerator _guidGen;
    private readonly UserManager<ApplicationUser> _userManager;

    public RevokeDelegationCommandHandler(
        IDelegationRepository delegations,
        ICacheService cache,
        IGuidGenerator guidGen,
        UserManager<ApplicationUser> userManager)
    {
        _delegations = delegations;
        _cache = cache;
        _guidGen = guidGen;
        _userManager = userManager;
    }

    public async Task<Result> Handle(RevokeDelegationCommand cmd, CancellationToken ct)
    {
        // T2: look up by PublicId (external addressing key)
        var delegation = await _delegations.GetByPublicIdAsync(cmd.DelegationId, ct);

        if (delegation is null)
            return Result.Fail(new NotFoundError($"Delegation {cmd.DelegationId} not found."));

        // Authorization: only the delegator or an Admin/SystemAdmin may revoke. Without this guard any
        // authenticated caller who learns a delegation's id could revoke delegations they do not own,
        // stripping others of legitimately delegated access.
        if (cmd.ActorId != delegation.DelegatorId)
        {
            var actor = await _userManager.FindByIdAsync(cmd.ActorId.ToString());
            var isAdmin = actor is not null
                && (await _userManager.IsInRoleAsync(actor, Roles.SystemAdmin)
                    || await _userManager.IsInRoleAsync(actor, Roles.Admin));
            if (!isAdmin)
                return Result.Fail(new ForbiddenError("Only the delegator or an administrator can revoke this delegation."));
        }

        if (delegation.IsRevoked)
            return Result.Fail(new ConflictError($"Delegation {cmd.DelegationId} is already revoked."));

        // Use domain method — encapsulates IsRevoked, RevokedBy, Status, raises DelegationRevokedEvent
        delegation.Revoke(cmd.ActorId, _guidGen);

        await _delegations.SaveChangesAsync(ct);

        // Invalidate delegation cache so ClaimsEnrichmentTransformer picks up change within TTL
        await _cache.RemoveAsync($"delegation:{delegation.DelegateId}", ct);

        // Domain events raised inside Revoke() — outbox interceptor dispatches on SaveChanges

        return Result.Ok();
    }
}
