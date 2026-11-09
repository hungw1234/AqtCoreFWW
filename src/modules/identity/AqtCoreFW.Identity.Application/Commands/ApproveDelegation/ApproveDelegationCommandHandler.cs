using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.ApproveDelegation;

/// <summary>
/// Transitions a PendingApproval delegation to Active.
/// Approval authority is the delegator themselves or a SystemAdmin override (NOT an arbitrary Admin),
/// and the approver may be neither the requester (CreatedBy) nor the delegate — 4-eyes prevents
/// self-approving an on-behalf delegation into Active.
/// Invalidates delegation Redis cache for the delegate so ClaimsEnrichmentTransformer
/// picks up the newly-active delegation within its TTL.
/// </summary>
public sealed class ApproveDelegationCommandHandler : IRequestHandler<ApproveDelegationCommand, Result>
{
    private readonly IDelegationRepository _delegations;
    private readonly ICacheService _cache;
    private readonly UserManager<ApplicationUser> _userManager;

    public ApproveDelegationCommandHandler(
        IDelegationRepository delegations,
        ICacheService cache,
        UserManager<ApplicationUser> userManager)
    {
        _delegations = delegations;
        _cache = cache;
        _userManager = userManager;
    }

    public async Task<Result> Handle(ApproveDelegationCommand cmd, CancellationToken ct)
    {
        var actor = await _userManager.FindByIdAsync(cmd.ActorId.ToString());
        if (actor is null)
            return Result.Fail(new ForbiddenError("Actor not found."));

        // T2: look up by PublicId (external addressing key)
        var delegation = await _delegations.GetByPublicIdAsync(cmd.DelegationId, ct);
        if (delegation is null)
            return Result.Fail(new NotFoundError($"Delegation {cmd.DelegationId} not found."));

        if (delegation.Status != DelegationStatus.PendingApproval)
            return Result.Fail(new ConflictError(
                $"Delegation {cmd.DelegationId} is not in PendingApproval state (current: {delegation.Status})."));

        // Approver authority: a PendingApproval delegation lends the DELEGATOR's authority (it was created
        // on behalf of a third party), so consent must come from the delegator themselves or a SystemAdmin
        // override — NOT from an arbitrary Admin. And the approver may be neither the requester (CreatedBy)
        // nor the delegate (the receiver): 4-eyes prevents self-approving an on-behalf delegation into
        // Active, which would otherwise let an Admin silently grant themselves a SystemAdmin's role set.
        var actorRoles = await _userManager.GetRolesAsync(actor);
        var isSystemAdmin = actorRoles.Contains("SystemAdmin");
        if (!isSystemAdmin && cmd.ActorId != delegation.DelegatorId)
            return Result.Fail(new ForbiddenError("Only the delegator or a SystemAdmin can approve this delegation."));

        if (cmd.ActorId == delegation.CreatedBy || cmd.ActorId == delegation.DelegateId)
            return Result.Fail(new ForbiddenError(
                "You cannot approve a delegation you requested or that grants authority to you."));

        // Use domain method — encapsulates Status, ApprovedBy, ApprovedAtUtc, audit trail
        delegation.Approve(cmd.ActorId);

        await _delegations.SaveChangesAsync(ct);

        // Invalidate delegation cache so claims transformer picks up new Active delegation
        await _cache.RemoveAsync($"delegation:{delegation.DelegateId}", ct);

        return Result.Ok();
    }
}
