using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.DeleteCredentialPolicy;

public sealed class DeleteCredentialPolicyCommandHandler(
    ICredentialPolicyRepository repository)
    : IRequestHandler<DeleteCredentialPolicyCommand, Result>
{
    public async Task<Result> Handle(DeleteCredentialPolicyCommand cmd, CancellationToken ct)
    {
        var policy = await repository.GetByIdAsync(cmd.Id, ct);
        if (policy is null)
            return Result.Fail(new NotFoundError($"CredentialPolicy {cmd.Id} not found"));

        await repository.DeleteAsync(policy, ct);
        return Result.Ok();
    }
}
