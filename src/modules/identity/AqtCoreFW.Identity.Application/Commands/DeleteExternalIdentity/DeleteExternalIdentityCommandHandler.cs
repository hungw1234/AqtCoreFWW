using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.DeleteExternalIdentity;

public sealed class DeleteExternalIdentityCommandHandler(
    IExternalIdentityRepository repository,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<DeleteExternalIdentityCommand, Result>
{
    public async Task<Result> Handle(DeleteExternalIdentityCommand cmd, CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(cmd.Id, ct);
        if (entity is null)
            return Result.Fail(new NotFoundError($"ExternalIdentity {cmd.Id} not found"));

        // Tenant isolation: the entity is keyed by a sequential bigint Id with no tenant column /
        // RLS, so a non-SystemAdmin caller could enumerate and unlink other tenants' users'
        // identities (lock-out / DoS). Bind to the owning user's tenant; report NotFound (not
        // Forbidden) so cross-tenant record existence is not leaked. SystemAdmin skips the bind.
        if (!cmd.IsPrivileged)
        {
            var owner = await userManager.FindByIdAsync(entity.UserId.ToString());
            if (owner is null
                || !(cmd.CallerTenantId.HasValue && owner.TenantId == cmd.CallerTenantId))
                return Result.Fail(new NotFoundError($"ExternalIdentity {cmd.Id} not found"));
        }

        await repository.DeleteAsync(entity, ct);
        return Result.Ok();
    }
}
