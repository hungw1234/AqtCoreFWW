using AqtCoreFW.ModuleName.Domain.Entities;
using AqtCoreFW.ModuleName.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.ModuleName.Application.Commands.CreateModuleName;

public sealed class CreateModuleNameCommandHandler(IModuleNameRepository repository)
    : IRequestHandler<CreateModuleNameCommand, Result<long>>
{
    public async Task<Result<long>> Handle(
        CreateModuleNameCommand request,
        CancellationToken cancellationToken)
    {
        var entity = ModuleNameEntity.Create(
            request.TenantId,
            request.Title,
            request.Description,
            request.CreatedBy);

        await repository.AddAsync(entity, cancellationToken);

        return Result.Ok(entity.Id);
    }
}
