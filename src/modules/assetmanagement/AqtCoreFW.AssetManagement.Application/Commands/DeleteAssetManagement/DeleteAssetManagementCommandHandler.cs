using AqtCoreFW.AssetManagement.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.AssetManagement.Application.Commands.DeleteAssetManagement;

public sealed class DeleteAssetManagementCommandHandler(IAssetManagementRepository repository)
    : IRequestHandler<DeleteAssetManagementCommand, Result>
{
    public async Task<Result> Handle(
        DeleteAssetManagementCommand request,
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
