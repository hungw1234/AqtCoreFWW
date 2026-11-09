using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Api.Controllers;

/// <summary>
/// Public read-only endpoints over the SharedKernel security namespace.
/// Plan 260523-1241 Phase 06a — exposes <see cref="PermCodes.All"/> so the workflow
/// designer (ByPermission clause editor) and other UIs can pick from the canonical
/// PermCode list without hand-maintaining a duplicate.
///
/// Authentication: required ([Authorize] with no policy). PermCode strings are public
/// identifiers (no secrets), but anonymous access would let attackers enumerate the
/// authorisation namespace.
/// </summary>
[Route("api/v1/security/perm-codes")]
[Authorize]
public sealed class SecurityController : ApiControllerBase
{
    /// <summary>
    /// GET /api/v1/security/perm-codes — returns the full canonical PermCode list
    /// (Trim + ToUpperInvariant convention enforced at the source list).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), 200)]
    public IActionResult List()
        => ToApiResponse(Result.Ok(PermCodes.All));
}
