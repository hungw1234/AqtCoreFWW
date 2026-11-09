using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.CreateRole;

public sealed class CreateRoleCommandHandler(
    IRoleRepository roleRepository,
    IPublisher publisher,
    IGuidGenerator guidGen)
    : IRequestHandler<CreateRoleCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand cmd, CancellationToken ct)
    {
        // Check Code+TenantId uniqueness
        if (await roleRepository.ExistsByCodeAndTenantAsync(cmd.Code, cmd.TenantId, ct))
            return Result.Fail<Guid>(new ConflictError($"Mã vai trò '{cmd.Code}' đã tồn tại."));

        var role = new ApplicationRole
        {
            Id = guidGen.NewIdRandom(),
            Name = cmd.Name,
            Code = cmd.Code,
            Description = cmd.Description,
            RoleType = cmd.RoleType,
            TenantId = cmd.TenantId,
            IsActive = true
        };

        await roleRepository.AddAsync(role, ct);

        await publisher.Publish(
            new RoleCreatedEvent(role.Id, role.Code, role.Name, cmd.ActorId, guidGen.NewIdRandom()), ct);

        return Result.Ok(role.Id);
    }
}
