using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.UpdateCredentialPolicy;

public sealed class UpdateCredentialPolicyCommandHandler(
    ICredentialPolicyRepository repository)
    : IRequestHandler<UpdateCredentialPolicyCommand, Result>
{
    public async Task<Result> Handle(UpdateCredentialPolicyCommand cmd, CancellationToken ct)
    {
        var policy = await repository.GetByIdAsync(cmd.Id, ct);
        if (policy is null)
            return Result.Fail(new NotFoundError($"CredentialPolicy {cmd.Id} not found"));

        policy.Update(
            cmd.Name, cmd.MinLength, cmd.MaxLength,
            cmd.RequireUppercase, cmd.RequireLowercase,
            cmd.RequireDigit, cmd.RequireSpecialChar,
            cmd.RotationDays, cmd.MaxFailedAttempts,
            cmd.LockoutMinutes, cmd.PasswordHistoryCount,
            cmd.IsDefault, cmd.ActorId,
            cmd.EscalationLockoutCount, cmd.EscalationWindowHours,
            cmd.ShowRemainingAttempts);

        await repository.UpdateAsync(policy, ct);
        return Result.Ok();
    }
}
