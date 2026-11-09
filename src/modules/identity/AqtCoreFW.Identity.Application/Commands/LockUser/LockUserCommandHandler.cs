using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.LockUser;

public sealed class LockUserCommandHandler : IRequestHandler<LockUserCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDomainEventPublisher _events;
    private readonly IGuidGenerator _guidGen;

    public LockUserCommandHandler(UserManager<ApplicationUser> userManager, IDomainEventPublisher events, IGuidGenerator guidGen)
    {
        _userManager = userManager;
        _events = events;
        _guidGen = guidGen;
    }

    public async Task<Result> Handle(LockUserCommand cmd, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(cmd.UserId.ToString());
        if (user is null)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found"));

        user.IsActive = !cmd.Lock;

        // ASP.NET Core Identity lockout — also set LockoutEnd for Lock=true
        await _userManager.SetLockoutEnabledAsync(user, cmd.Lock);
        if (cmd.Lock)
        {
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }
        else
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            // QĐ 742 1.4.d: admin unlock = full reset of escalation state
            user.AutoDisabledAt = null;
            user.ConsecutiveLockoutCount = 0;
            user.LastLockoutAt = null;
        }

        await _userManager.UpdateAsync(user);

        await _events.PublishAsync([new UserLockedEvent(cmd.UserId, cmd.Lock, cmd.ActorId, _guidGen.NewIdRandom())], ct);
        return Result.Ok();
    }
}
