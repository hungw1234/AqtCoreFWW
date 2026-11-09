using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.ActivateRole;

public sealed class ActivateRoleCommandHandler(
    IRoleRepository roleRepository,
    IBulkRoleChangeService bulkRoleChangeService,
    IPublisher publisher,
    IGuidGenerator guidGen)
    : IRequestHandler<ActivateRoleCommand, Result>
{
    public async Task<Result> Handle(ActivateRoleCommand cmd, CancellationToken ct)
    {
        var role = await roleRepository.GetByIdAsync(cmd.RoleId, ct);
        if (role is null)
            return Result.Fail(new NotFoundError("Vai trò không tồn tại."));

        // C-02: Tenant isolation
        if (role.TenantId is not null && role.TenantId != cmd.TenantId)
            return Result.Fail(new ForbiddenError("Không có quyền kích hoạt vai trò này."));

        if (role.IsActive)
            return Result.Ok(); // Already active — idempotent

        role.IsActive = true;
        await roleRepository.UpdateAsync(role, ct);

        // Refresh caches so re-activated role permissions take effect
        await bulkRoleChangeService.InvalidateRoleCacheForAllUsersAsync();

        await publisher.Publish(
            new RoleActivatedEvent(role.Id, cmd.ActorId, guidGen.NewIdRandom()), ct);

        return Result.Ok();
    }
}
