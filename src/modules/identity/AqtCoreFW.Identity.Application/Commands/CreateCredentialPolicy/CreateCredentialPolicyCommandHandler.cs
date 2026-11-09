using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.CreateCredentialPolicy;

public sealed class CreateCredentialPolicyCommandHandler(
    ICredentialPolicyRepository repository)
    : IRequestHandler<CreateCredentialPolicyCommand, Result<long>>
{
    public async Task<Result<long>> Handle(CreateCredentialPolicyCommand cmd, CancellationToken ct)
    {
        var policy = CredentialPolicy.Create(
            cmd.Name, cmd.TenantId,
            cmd.MinLength, cmd.MaxLength,
            cmd.RequireUppercase, cmd.RequireLowercase,
            cmd.RequireDigit, cmd.RequireSpecialChar,
            cmd.RotationDays, cmd.MaxFailedAttempts,
            cmd.LockoutMinutes, cmd.PasswordHistoryCount,
            cmd.IsDefault, cmd.ActorId,
            cmd.EscalationLockoutCount, cmd.EscalationWindowHours,
            cmd.ShowRemainingAttempts);

        await repository.AddAsync(policy, ct);
        // T1: return long Id (DB-assigned IDENTITY after SaveChanges in AddAsync)
        return Result.Ok(policy.Id);
    }
}
