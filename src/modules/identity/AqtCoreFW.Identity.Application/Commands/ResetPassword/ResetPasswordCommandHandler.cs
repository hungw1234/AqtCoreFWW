using AqtCoreFW.Identity.Application.Commands.RevokeToken;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.ResetPassword;

public sealed class ResetPasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    ISender sender,
    IDomainEventPublisher events,
    IGuidGenerator gen) : IRequestHandler<ResetPasswordCommand, Result>
{
    public async Task<Result> Handle(ResetPasswordCommand cmd, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(cmd.UserId.ToString());
        if (user is null)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found"));

        if (string.IsNullOrEmpty(user.Email))
            return Result.Fail("User has no email address configured.");

        // Generate secure reset token
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        // Security (F-09): revoke all active tokens so old sessions cannot be reused
        await sender.Send(new RevokeTokenCommand(TokenId: null, UserId: cmd.UserId, ActorId: cmd.UserId), ct);

        // Security (F-25): publish event — Notifications module emails the token.
        // Token NEVER returned in API response.
        await events.PublishAsync(
            [new PasswordResetRequestedEvent(cmd.UserId, user.Email, user.FullName, token, gen.NewIdMonotonic())], ct);

        return Result.Ok();
    }
}
