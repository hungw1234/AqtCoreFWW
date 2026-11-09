using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.CreateTenant;

public sealed class CreateTenantCommandHandler(
    ITenantRepository tenantRepository,
    IGuidGenerator guidGen,
    IPublisher publisher)
    : IRequestHandler<CreateTenantCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateTenantCommand cmd, CancellationToken ct)
    {
        // Check if Code already exists
        if (await tenantRepository.ExistsByCodeAsync(cmd.Code, ct))
        {
            return Result.Fail(new ConflictError($"Mã tenant '{cmd.Code}' đã tồn tại."));
        }

        // Create tenant via factory (T2 — IGuidGenerator for PublicId)
        var tenant = Tenant.Create(guidGen, cmd.Name, cmd.Code, cmd.ContactEmail, cmd.ActorId);

        // Persist
        await tenantRepository.AddAsync(tenant, ct);

        // Publish domain events via non-generic overload so MediatR dispatches
        // by runtime type to the concrete INotificationHandler<TEvent>.
        foreach (var domainEvent in tenant.DomainEvents)
        {
            await publisher.Publish((object)domainEvent, ct);
        }
        tenant.ClearDomainEvents();

        // T2: return PublicId (Guid AK) — the external addressing key
        return Result.Ok(tenant.PublicId);
    }
}
