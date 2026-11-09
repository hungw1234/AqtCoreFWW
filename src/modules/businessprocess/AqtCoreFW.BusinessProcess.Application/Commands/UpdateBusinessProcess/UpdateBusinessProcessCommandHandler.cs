using AqtCoreFW.BusinessProcess.Application.Caching;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Commands.UpdateBusinessProcess;

/// <summary>
/// Pattern A (R13): strict tenant guard pre-DB. Repo.UpdateAsync also guards (defense in depth).
/// Cache invalidation is broad-prefix per tenant — see <see cref="CreateBusinessProcess.CreateBusinessProcessCommandHandler"/> note.
/// </summary>
public sealed class UpdateBusinessProcessCommandHandler(
    IBusinessProcessRepository repository,
    ITenantContext tenantContext,
    ICacheService cache,
    ILogger<UpdateBusinessProcessCommandHandler> logger)
    : IRequestHandler<UpdateBusinessProcessCommand, Result>
{
    public async Task<Result> Handle(UpdateBusinessProcessCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant UpdateBusinessProcess denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var bp = await repository.GetByPublicIdAsync(request.BusinessProcessPublicId, cancellationToken);
        if (bp is null)
            return Result.Fail(new NotFoundError(
                $"{BusinessProcessErrorCodes.NotFound}: BusinessProcess {request.BusinessProcessPublicId} không tìm thấy."));

        bp.UpdateMetadata(request.Name, request.Description, request.ModifiedBy);
        await repository.UpdateAsync(bp, cancellationToken);
        await cache.RemoveByPrefixAsync(BusinessProcessCacheKeys.TenantPrefix(request.TenantId), cancellationToken);

        logger.LogInformation(
            "BusinessProcess updated: publicId={PublicId}, tenantId={TenantId}, modifiedBy={ModifiedBy}",
            bp.PublicId, bp.TenantId, request.ModifiedBy);

        return Result.Ok();
    }
}
