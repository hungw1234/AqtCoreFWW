using AqtCoreFW.Identity.Application.Queries.SearchUsers;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Public user search — any authenticated user can find colleagues in same tenant.
/// Returns minimal fields only (id, fullName, departmentCode) — no email (PII).
/// Used by chat module to pick conversation participants.
/// </summary>
[Route("api/v1/users/search")]
[Authorize]
[EnableRateLimiting("read-ops")]
public sealed class UserSearchController(ISender mediator) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new SearchUsersQuery(q, pageSize, ResolveTenantId(), ResolveUserId()), ct);

        return ToApiResponse(result);
    }
}
