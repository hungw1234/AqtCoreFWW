using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Returns the active signing provider info for a tenant, scoped to the caller's own tenant
/// (resolved from JWT — never client-supplied). This is the non-admin, non-MFA read that the
/// form-fill flow needs to open the signing modal: it must know WHICH provider drives the
/// tenant (to pick the correct client adapter — USB-local vs remote OTP) and that provider's
/// capabilities (to detect RequiresLocalAgent). The admin config endpoint is SystemAdmin-only,
/// so a regular form filler cannot read it; this query exposes only the minimal,
/// non-sensitive subset required for client-side signing routing.
/// </summary>
public sealed record GetSigningProviderInfoQuery(Guid TenantId)
    : IRequest<Result<SigningProviderInfoDto>>;

public sealed class GetSigningProviderInfoQueryHandler(
    ITenantSignatureConfigRepository configRepository,
    ISignatureProviderResolver resolver)
    : IRequestHandler<GetSigningProviderInfoQuery, Result<SigningProviderInfoDto>>
{
    public async Task<Result<SigningProviderInfoDto>> Handle(
        GetSigningProviderInfoQuery request, CancellationToken ct)
    {
        var config = await configRepository.GetByTenantIdAsync(request.TenantId, ct);

        // No config or signing disabled → report disabled so the FE renders an unavailable state
        // rather than treating it as an error. providerKey absent, no capabilities to expose.
        if (config is not { IsEnabled: true })
            return Result.Ok(new SigningProviderInfoDto(false, null, SignatureProviderCapabilities.None));

        // Resolve the registered provider's capabilities. A configured-but-unregistered provider
        // key (deploy drift) is reported as enabled-with-no-capabilities rather than a 500 — the
        // signing modal then surfaces the missing-adapter state to the user.
        SignatureProviderCapabilities capabilities;
        try
        {
            capabilities = resolver.Resolve(config.ProviderKey).Capabilities;
        }
        catch (KeyNotFoundException)
        {
            capabilities = SignatureProviderCapabilities.None;
        }

        return Result.Ok(new SigningProviderInfoDto(true, config.ProviderKey, capabilities));
    }
}
