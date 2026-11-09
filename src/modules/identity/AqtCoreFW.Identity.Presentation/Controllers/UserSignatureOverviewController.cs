using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Self-service read endpoint aggregating the authenticated user's signature profiles + enrollment
/// status in one call. Exists because the OIDC claims carry neither the signing national-id state nor
/// the enrollment lifecycle (see Phase 6). Identity (TenantId + UserId) is resolved from the JWT
/// server-side and never body/route-bound; <see cref="ApiControllerBase.ResolveTenantId"/> throws
/// (→ 401) when the tenant claim is missing (fail-closed).
/// </summary>
[Route("api/v1/me/signature-overview")]
[Authorize]
public sealed class UserSignatureOverviewController(ISender mediator) : ApiControllerBase
{
    /// <summary>Returns the caller's profiles + enrollment status (national-id + approved-enrollment flags).</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(new SignatureOverviewQuery(tenantId, userId), ct);
        return ToApiResponse(result);
    }
}
