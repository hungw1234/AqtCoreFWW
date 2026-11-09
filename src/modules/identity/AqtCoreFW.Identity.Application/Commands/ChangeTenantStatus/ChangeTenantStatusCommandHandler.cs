using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.ChangeTenantStatus;

public sealed class ChangeTenantStatusCommandHandler(
    ITenantRepository tenantRepository,
    IPublisher publisher,
    IGuidGenerator guidGen)
    : IRequestHandler<ChangeTenantStatusCommand, Result>
{
    public async Task<Result> Handle(ChangeTenantStatusCommand cmd, CancellationToken ct)
    {
        // T2: look up by PublicId (Guid AK — external addressing key)
        var tenant = await tenantRepository.GetByPublicIdAsync(cmd.TenantId, ct);
        if (tenant is null)
        {
            return Result.Fail(new NotFoundError($"Tenant với Id '{cmd.TenantId}' không tồn tại."));
        }

        // Change status (no-op if same)
        tenant.ChangeStatus(cmd.NewStatus, cmd.ActorId, guidGen);

        // TODO (Future enhancement): When status changes to Suspended or Archived,
        // consider blocking active JWT sessions. This requires auth pipeline interceptor
        // to check tenant status on every request. Not in scope for Phase 02.

        // Persist
        await tenantRepository.UpdateAsync(tenant, ct);

        // Publish domain events via non-generic overload so MediatR dispatches
        // by runtime type to the concrete INotificationHandler<TEvent>.
        foreach (var domainEvent in tenant.DomainEvents)
        {
            await publisher.Publish((object)domainEvent, ct);
        }
        tenant.ClearDomainEvents();

        return Result.Ok();
    }
}
