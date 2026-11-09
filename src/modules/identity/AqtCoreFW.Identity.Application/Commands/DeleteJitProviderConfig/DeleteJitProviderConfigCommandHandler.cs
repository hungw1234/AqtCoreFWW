using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.DeleteJitProviderConfig;

public sealed class DeleteJitProviderConfigCommandHandler(
    IJitProviderConfigRepository repository)
    : IRequestHandler<DeleteJitProviderConfigCommand, Result>
{
    public async Task<Result> Handle(DeleteJitProviderConfigCommand cmd, CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"JitProviderConfig {cmd.Id} not found"));

        entity.Deactivate(cmd.ActorId);
        await repository.UpdateAsync(entity, ct);
        return Result.Ok();
    }
}
