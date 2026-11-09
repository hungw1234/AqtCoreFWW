using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.DeleteRole;

public sealed class DeleteRoleCommandHandler(
    IRoleRepository roleRepository,
    IBulkRoleChangeService bulkRoleChangeService,
    IPublisher publisher,
    IGuidGenerator guidGen)
    : IRequestHandler<DeleteRoleCommand, Result>
{
    public async Task<Result> Handle(DeleteRoleCommand cmd, CancellationToken ct)
    {
        var role = await roleRepository.GetByIdAsync(cmd.RoleId, ct);
        if (role is null)
            return Result.Fail(new NotFoundError("Vai trò không tồn tại."));

        // C-02: Tenant isolation
        if (role.TenantId is not null && role.TenantId != cmd.TenantId)
            return Result.Fail(new ForbiddenError("Không có quyền xóa vai trò này."));

        if (role.RoleType == RoleType.System)
            return Result.Fail(new ForbiddenError("Không thể xóa vai trò hệ thống."));

        await roleRepository.SoftDeleteAsync(role, ct);

        // RT-05: Invalidate permission caches for all users after role deactivation
        await bulkRoleChangeService.InvalidateRoleCacheForAllUsersAsync();

        await publisher.Publish(
            new RoleDeactivatedEvent(role.Id, cmd.ActorId, guidGen.NewIdRandom()), ct);

        return Result.Ok();
    }
}
