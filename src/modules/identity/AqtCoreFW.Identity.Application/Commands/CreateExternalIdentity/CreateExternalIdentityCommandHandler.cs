using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.CreateExternalIdentity;

public sealed class CreateExternalIdentityCommandHandler(
    IExternalIdentityRepository repository,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<CreateExternalIdentityCommand, Result<long>>
{
    public async Task<Result<long>> Handle(CreateExternalIdentityCommand cmd, CancellationToken ct)
    {
        // Verify the user exists
        var user = await userManager.FindByIdAsync(cmd.UserId.ToString());
        if (user is null)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found"));

        // Tenant isolation: ExternalIdentities has no TenantId column / RLS, and the EF context
        // applies no global tenant filter — so a non-SystemAdmin caller could otherwise link an
        // identity to a user in another tenant (account-takeover vector). Bind to the owning user's
        // tenant; report NotFound (not Forbidden) so cross-tenant user existence is not leaked.
        if (!cmd.IsPrivileged
            && !(cmd.CallerTenantId.HasValue && user.TenantId == cmd.CallerTenantId))
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found"));

        // Prevent duplicate provider link
        var existing = await repository.GetByUserAndProviderAsync(cmd.UserId, cmd.Provider, ct);
        if (existing is not null)
            return Result.Fail(new ConflictError(
                $"User {cmd.UserId} already has a linked {cmd.Provider} identity"));

        var entity = ExternalIdentity.Create(
            cmd.UserId, cmd.Provider, cmd.ExternalId,
            cmd.DisplayName, cmd.Email, cmd.ActorId);

        await repository.AddAsync(entity, ct);
        // T1: return long Id (DB-assigned IDENTITY after SaveChanges in AddAsync)
        return Result.Ok(entity.Id);
    }
}
