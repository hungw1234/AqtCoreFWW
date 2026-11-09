using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.UpdateTenant;

public sealed class UpdateTenantCommandHandler(
    ITenantRepository tenantRepository,
    IPublisher publisher)
    : IRequestHandler<UpdateTenantCommand, Result>
{
    public async Task<Result> Handle(UpdateTenantCommand cmd, CancellationToken ct)
    {
        // T2: look up by PublicId (Guid AK — external addressing key)
        var tenant = await tenantRepository.GetByPublicIdAsync(cmd.TenantId, ct);
        if (tenant is null)
        {
            return Result.Fail(new NotFoundError($"Tenant với Id '{cmd.TenantId}' không tồn tại."));
        }

        // Update mutable properties (Code is IMMUTABLE, Status via ChangeTenantStatusCommand)
        tenant.Update(cmd.Name, cmd.ContactEmail, cmd.ActorId);

        // Persist
        await tenantRepository.UpdateAsync(tenant, ct);

        // Publish domain events
        foreach (var domainEvent in tenant.DomainEvents)
        {
            await publisher.Publish(domainEvent, ct);
        }
        tenant.ClearDomainEvents();

        return Result.Ok();
    }
}
