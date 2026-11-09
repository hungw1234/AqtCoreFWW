using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetCredentialPolicyById;

public sealed class GetCredentialPolicyByIdQueryHandler(
    ICredentialPolicyRepository repository)
    : IRequestHandler<GetCredentialPolicyByIdQuery, Result<CredentialPolicyDto>>
{
    public async Task<Result<CredentialPolicyDto>> Handle(
        GetCredentialPolicyByIdQuery query, CancellationToken ct)
    {
        var policy = await repository.GetByIdAsync(query.Id, ct);
        if (policy is null)
            return Result.Fail<CredentialPolicyDto>(
                new NotFoundError($"CredentialPolicy {query.Id} not found"));

        // T1: DTO.Id mapped from entity.Id (long IDENTITY)
        return Result.Ok(new CredentialPolicyDto(
            policy.Id, policy.Name, policy.TenantId,
            policy.MinLength, policy.MaxLength,
            policy.RequireUppercase, policy.RequireLowercase,
            policy.RequireDigit, policy.RequireSpecialChar,
            policy.RotationDays, policy.MaxFailedAttempts,
            policy.LockoutMinutes, policy.PasswordHistoryCount,
            policy.IsDefault,
            policy.EscalationLockoutCount, policy.EscalationWindowHours,
            policy.ShowRemainingAttempts));
    }
}
