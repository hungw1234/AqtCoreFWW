using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.UpdateJitProviderConfig;

public sealed class UpdateJitProviderConfigCommandHandler(
    IJitProviderConfigRepository repository)
    : IRequestHandler<UpdateJitProviderConfigCommand, Result>
{
    public async Task<Result> Handle(UpdateJitProviderConfigCommand cmd, CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"JitProviderConfig {cmd.Id} not found"));

        entity.Update(
            cmd.DisplayName, cmd.JitEnabled, cmd.DefaultRoleName,
            cmd.RequireApproval, cmd.ClaimMappingJson,
            cmd.DefaultTenantId, cmd.AllowedDomainsJson,
            cmd.MaxProvisionsPerHour, cmd.ActorId);

        await repository.UpdateAsync(entity, ct);
        return Result.Ok();
    }
}
