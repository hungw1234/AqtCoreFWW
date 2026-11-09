using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.UpdateRole;

public sealed class UpdateRoleCommandHandler(
    IRoleRepository roleRepository,
    IPublisher publisher,
    IGuidGenerator guidGen)
    : IRequestHandler<UpdateRoleCommand, Result>
{
    public async Task<Result> Handle(UpdateRoleCommand cmd, CancellationToken ct)
    {
        var role = await roleRepository.GetByIdAsync(cmd.RoleId, ct);
        if (role is null)
            return Result.Fail(new NotFoundError("Vai trò không tồn tại."));

        // C-02: Tenant isolation — verify ownership
        if (role.TenantId is not null && role.TenantId != cmd.TenantId)
            return Result.Fail(new ForbiddenError("Không có quyền chỉnh sửa vai trò này."));

        // System roles: cannot rename
        if (role.RoleType == RoleType.System && role.Name != cmd.Name)
            return Result.Fail(new ForbiddenError("Không thể đổi tên vai trò hệ thống."));

        role.Name = cmd.Name;
        role.Description = cmd.Description;

        await roleRepository.UpdateAsync(role, ct);

        await publisher.Publish(
            new RoleUpdatedEvent(role.Id, role.Name, cmd.ActorId, guidGen.NewIdRandom()), ct);

        return Result.Ok();
    }
}
