using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>Returns the current signature provider config for a tenant, or NotFound if not yet configured.</summary>
public sealed record GetTenantSignatureConfigQuery(Guid TenantId)
    : IRequest<Result<TenantSignatureConfigDto>>;

public sealed class GetTenantSignatureConfigQueryHandler(
    ITenantSignatureConfigRepository repository)
    : IRequestHandler<GetTenantSignatureConfigQuery, Result<TenantSignatureConfigDto>>
{
    public async Task<Result<TenantSignatureConfigDto>> Handle(
        GetTenantSignatureConfigQuery request, CancellationToken ct)
    {
        var cfg = await repository.GetByTenantIdAsync(request.TenantId, ct);
        if (cfg is null)
            return Result.Fail(new NotFoundError(
                $"No signature config found for tenant {request.TenantId}."));

        return Result.Ok(new TenantSignatureConfigDto(
            cfg.TenantId,
            cfg.ProviderKey,
            cfg.ApiCredentialsRef,
            cfg.AllowedCaRootThumbprints,
            cfg.OcspMode,
            cfg.IsEnabled,
            cfg.SignFlowMode,
            Convert.ToBase64String(cfg.RowVersion),
            cfg.CreatedAt,
            cfg.UpdatedAt));
    }
}
