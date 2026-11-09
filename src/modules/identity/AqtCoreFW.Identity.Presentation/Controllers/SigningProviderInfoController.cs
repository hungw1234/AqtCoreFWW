using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Non-admin signing-provider discovery for the form-fill signing flow.
///
/// Deliberately a separate controller from <see cref="DigitalSignatureController"/>: the latter
/// carries a class-level <c>[RequiresMfa]</c> and <c>write-ops</c> rate limit appropriate for the
/// sign/verify operations. Provider discovery is a low-risk read that any authenticated form
/// filler must perform before signing, so it requires <c>[Authorize]</c> only — no MFA, no admin
/// policy. Tenant is resolved from the JWT, never from the request, so a caller can only read
/// their own tenant's provider info.
/// </summary>
[Route("api/v1/digital-signature")]
[Authorize]
public sealed class SigningProviderInfoController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// GET /api/v1/digital-signature/provider-info
    /// Returns { isEnabled, providerKey, capabilities } for the caller's tenant. Always 200:
    /// a tenant with no/disabled config returns isEnabled=false rather than 404.
    /// </summary>
    [HttpGet("provider-info")]
    [ProducesResponseType(typeof(SigningProviderInfoDto), 200)]
    public async Task<IActionResult> GetProviderInfo(CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetSigningProviderInfoQuery(ResolveTenantId()), ct));
}
