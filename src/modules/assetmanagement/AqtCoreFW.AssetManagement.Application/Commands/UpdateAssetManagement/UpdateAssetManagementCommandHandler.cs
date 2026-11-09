using AqtCoreFW.AssetManagement.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.AssetManagement.Application.Commands.UpdateAssetManagement;

public sealed class UpdateAssetManagementCommandHandler(IAssetManagementRepository repository)
    : IRequestHandler<UpdateAssetManagementCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        UpdateAssetManagementCommand request,
        CancellationToken cancellationToken)
    {
        var result = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (result.IsFailed)
            return Result.Fail<long>(result.Errors);

        var entity = result.Value;
        entity.Update(request.Title, request.Description, request.UpdatedBy);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Ok(entity.Id);
    }
}
