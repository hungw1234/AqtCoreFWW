using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Integration.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Integration.Application.Commands.DeleteContract;

public sealed class DeleteContractCommandHandler(IContractRepository repository)
    : IRequestHandler<DeleteContractCommand, Result>
{
    public async Task<Result> Handle(
        DeleteContractCommand request, CancellationToken cancellationToken)
    {
        // T2 entity: request.Id is PublicId (Guid AK) — use GetByPublicIdAsync scoped to tenant.
        var contract = await repository.GetByPublicIdAsync(request.Id, request.TenantId, cancellationToken);
        if (contract is null)
            return Result.Fail(new NotFoundError($"Contract {request.Id} not found."));

        contract.Delete();
        await repository.UpdateAsync(contract, cancellationToken);
        return Result.Ok();
    }
}
