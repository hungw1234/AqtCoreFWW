using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.UpdateExternalIdentity;

public sealed class UpdateExternalIdentityCommandHandler(
    IExternalIdentityRepository repository,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<UpdateExternalIdentityCommand, Result>
{
    public async Task<Result> Handle(UpdateExternalIdentityCommand cmd, CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"ExternalIdentity {cmd.Id} not found"));

        // Tenant isolation: the entity is keyed by a sequential bigint Id with no tenant column /
        // RLS, so a non-SystemAdmin caller could enumerate and mutate links owned by other tenants'
        // users. Bind to the owning user's tenant; report NotFound (not Forbidden) so cross-tenant
        // record existence is not leaked. SystemAdmin skips the bind (cross-tenant escape).
        if (!cmd.IsPrivileged)
        {
            var owner = await userManager.FindByIdAsync(entity.UserId.ToString());
            if (owner is null
                || !(cmd.CallerTenantId.HasValue && owner.TenantId == cmd.CallerTenantId))
                return Result.Fail(new NotFoundError($"ExternalIdentity {cmd.Id} not found"));
        }

        entity.UpdateSync(cmd.DisplayName, cmd.Email, cmd.Metadata, cmd.ActorId);
        await repository.UpdateAsync(entity, ct);
        return Result.Ok();
    }
}
