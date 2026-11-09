using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.SharedKernel.Application.Behaviors;

/// <summary>
/// Validates ITenantContext.TenantId is set before any command handler runs.
/// Registered FIRST in MediatR pipeline (before ValidationBehavior).
/// SystemAdmin role bypasses tenant check for cross-tenant operations.
/// </summary>
public sealed class TenantAwareBehavior<TRequest, TResponse>(ITenantContext tenantContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only enforce on commands (write operations) — queries allowed without tenant
        if (request is not IBaseCommand)
            return await next();

        // A command explicitly marked tenant-agnostic (system-to-system, tenant derived from a
        // persisted entity rather than the principal) opts out of ambient-tenant enforcement and owns
        // its own tenant safety. Opt-in only — see ITenantAgnosticCommand.
        if (request is ITenantAgnosticCommand)
            return await next();

        if (!tenantContext.IsSystemAdmin && !tenantContext.TenantId.HasValue)
        {
            var error = new Error("Tenant context is required for this operation.")
                .WithMetadata("ErrorCode", ErrorCodes.Security.TenantRequired);

            return ResultBehaviorHelper.CreateFail<TResponse>([error]);
        }

        return await next();
    }
}
