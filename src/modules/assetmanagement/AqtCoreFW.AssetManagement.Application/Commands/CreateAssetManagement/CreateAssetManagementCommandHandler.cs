using AqtCoreFW.AssetManagement.Domain.Entities;
using AqtCoreFW.AssetManagement.Domain.Repositories;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.AssetManagement.Application.Commands.CreateAssetManagement;

public sealed class CreateAssetManagementCommandHandler(
    IAssetManagementRepository repository,
    IFeatureFlagService featureFlags)
    : IRequestHandler<CreateAssetManagementCommand, Result<long>>
{
    private const string CreateFlag = "feature:assetmanagement.create";

    public async Task<Result<long>> Handle(
        CreateAssetManagementCommand request,
        CancellationToken cancellationToken)
    {
        if (!featureFlags.IsEnabled(CreateFlag, request.TenantId.ToString()))
            return Result.Fail<long>(new ForbiddenError("Asset management creation is disabled."));

        var entity = AssetManagementEntity.Create(
            request.TenantId,
            request.Title,
            request.Description,
            request.CreatedBy);

        await repository.AddAsync(entity, cancellationToken);

        return Result.Ok(entity.Id);
    }
}
