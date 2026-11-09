using AqtCoreFW.BusinessProcess.Application.Caching;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Commands.DeactivateBusinessProcess;

/// <summary>
/// Pattern A (R13): strict tenant guard pre-DB; repo guards on UpdateAsync (defense in depth).
/// Aggregate's Deactivate emits <c>BusinessProcessDeactivatedEvent</c> only on real state change —
/// OutboxInterceptor routes it to <c>[businessprocess].OutboxMessages</c> in the same transaction.
/// </summary>
public sealed class DeactivateBusinessProcessCommandHandler(
    IBusinessProcessRepository repository,
    IGuidGenerator gen,
    ITenantContext tenantContext,
    ICacheService cache,
    ILogger<DeactivateBusinessProcessCommandHandler> logger)
    : IRequestHandler<DeactivateBusinessProcessCommand, Result>
{
    public async Task<Result> Handle(DeactivateBusinessProcessCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant DeactivateBusinessProcess denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var bp = await repository.GetByPublicIdAsync(request.BusinessProcessPublicId, cancellationToken);
        if (bp is null)
            return Result.Fail(new NotFoundError(
                $"{BusinessProcessErrorCodes.NotFound}: BusinessProcess {request.BusinessProcessPublicId} không tìm thấy."));

        var wasActive = bp.IsActive;
        bp.Deactivate(gen, request.ModifiedBy);
        await repository.UpdateAsync(bp, cancellationToken);
        await cache.RemoveByPrefixAsync(BusinessProcessCacheKeys.TenantPrefix(request.TenantId), cancellationToken);

        logger.LogInformation(
            "BusinessProcess deactivated: publicId={PublicId}, tenantId={TenantId}, modifiedBy={ModifiedBy}, transition={Transition}",
            bp.PublicId, bp.TenantId, request.ModifiedBy, wasActive ? "true->false" : "no-op");

        return Result.Ok();
    }
}
