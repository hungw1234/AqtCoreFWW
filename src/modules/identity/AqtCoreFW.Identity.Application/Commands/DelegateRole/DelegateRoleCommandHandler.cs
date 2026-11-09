using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.DelegateRole;

public sealed class DelegateRoleCommandHandler : IRequestHandler<DelegateRoleCommand, Result<Guid>>
{
    private readonly IDelegationRepository _delegations;
    private readonly IGuidGenerator _guidGen;
    private readonly UserManager<ApplicationUser> _userManager;

    public DelegateRoleCommandHandler(
        IDelegationRepository delegations,
        IGuidGenerator guidGen,
        UserManager<ApplicationUser> userManager)
    {
        _delegations = delegations;
        _guidGen = guidGen;
        _userManager = userManager;
    }

    public async Task<Result<Guid>> Handle(DelegateRoleCommand cmd, CancellationToken ct)
    {
        if (cmd.ValidFrom >= cmd.ValidTo)
            return Result.Fail(new ValidationError("ValidFrom must be before ValidTo."));

        if (cmd.ValidTo <= DateTime.UtcNow)
            return Result.Fail(new ValidationError("ValidTo must be in the future."));

        if (cmd.DelegatorId == cmd.DelegateId)
            return Result.Fail(new ValidationError("Cannot delegate to yourself."));

        // C-03: Block circular delegation — if B→A already exists, reject A→B
        var reverseExists = await _delegations.HasActiveOverlapAsync(
            cmd.DelegateId,     // reverse: delegate becomes delegator
            cmd.DelegatorId,    // reverse: delegator becomes delegate
            cmd.ValidFrom,
            cmd.ValidTo,
            ct);

        if (reverseExists)
            return Result.Fail(new ConflictError(
                "Circular delegation detected: the delegate already has an active delegation to the delegator. " +
                "Phát hiện ủy quyền vòng tròn: người được ủy quyền đã có ủy quyền ngược lại cho người ủy quyền."));

        // F-08: ownership check — actor can only create delegations for themselves unless Admin/SystemAdmin
        if (cmd.DelegatorId != cmd.ActorId)
        {
            var actor = await _userManager.FindByIdAsync(cmd.ActorId.ToString());
            if (actor is null)
                return Result.Fail(new ForbiddenError("Actor not found."));

            var actorRoles = await _userManager.GetRolesAsync(actor);
            if (!actorRoles.Any(r => r is "Admin" or "SystemAdmin"))
                return Result.Fail(new ForbiddenError("You can only create delegations for yourself."));
        }

        var hasDuplicate = await _delegations.HasActiveOverlapAsync(
            cmd.DelegatorId, cmd.DelegateId, cmd.ValidFrom, cmd.ValidTo, ct);

        if (hasDuplicate)
            return Result.Fail(new ConflictError(
                $"An active delegation from {cmd.DelegatorId} to {cmd.DelegateId} already overlaps the requested window."));

        // T2: Use domain factory — sets PublicId via IGuidGenerator, raises DelegationGrantedEvent
        var delegation = UserDelegation.Create(
            _guidGen,
            cmd.DelegatorId,
            cmd.DelegateId,
            cmd.ValidFrom,
            cmd.ValidTo,
            cmd.Reason,
            delegatedRoleIds: null,
            scopeJson: null,
            // Self-delegation (actor lends their OWN authority) activates immediately. Delegating on
            // behalf of someone else (actor != delegator — only Admin/SystemAdmin reach here) lends a
            // third party's authority, so it stays PendingApproval until the delegator or a SystemAdmin
            // approves — closing the silent Admin→SystemAdmin self-grant path.
            requiresApproval: cmd.DelegatorId != cmd.ActorId,
            cmd.ActorId);

        await _delegations.AddAsync(delegation, ct);
        await _delegations.SaveChangesAsync(ct);

        // Domain events already raised inside UserDelegation.Create — no manual publish needed.
        // Outbox interceptor dispatches them on SaveChanges.

        // T2: return PublicId (Guid AK) — the external addressing key
        return Result.Ok(delegation.PublicId);
    }
}
