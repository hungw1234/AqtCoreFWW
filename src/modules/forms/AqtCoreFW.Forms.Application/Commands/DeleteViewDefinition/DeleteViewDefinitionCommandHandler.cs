using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.DeleteViewDefinition;

public sealed class DeleteViewDefinitionCommandHandler(IViewDefinitionRepository repository)
    : IRequestHandler<DeleteViewDefinitionCommand, Result>
{
    public async Task<Result> Handle(
        DeleteViewDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        var view = await repository.GetByIdWithDetailsAsync(
            request.Id, request.TenantId, cancellationToken);

        if (view is null)
            return Result.Fail(new NotFoundError($"ViewDefinition {request.Id} không tìm thấy."));

        await repository.DeleteAsync(request.Id, cancellationToken);
        return Result.Ok();
    }
}
