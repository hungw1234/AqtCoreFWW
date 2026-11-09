using AqtCoreFW.Contracts.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Contracts.Application.Commands.DeleteContract;

public sealed class DeleteContractCommandHandler(IContractRepository repository)
    : IRequestHandler<DeleteContractCommand, Result>
{
    public async Task<Result> Handle(
        DeleteContractCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(result.Errors);

        result.Value.Delete();
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
