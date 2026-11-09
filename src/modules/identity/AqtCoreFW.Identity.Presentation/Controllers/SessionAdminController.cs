using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.RevokeToken;
using AqtCoreFW.Identity.Application.Queries.ListActiveSessions;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin endpoints for session management â€” view active tokens and force-revoke them.
/// Requires Admin or SystemAdmin role.
/// </summary>
[Route("api/v1/admin/sessions")]
[Authorize(Policy = PermCodes.Identity.Session.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class SessionAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>List active (non-expired, non-revoked) access tokens, server-paginated.</summary>
    [HttpGet("active")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetActive(
        [FromQuery] Guid? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(new ListActiveSessionsQuery(userId, page, pageSize), ct));
    }

    /// <summary>Revoke a single token by its OpenIddict token ID.</summary>
    [HttpDelete("{tokenId}")]
    [Authorize(Policy = PermCodes.Identity.Session.Revoke)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RevokeToken(string tokenId, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new RevokeTokenCommand(tokenId, UserId: null, GetActorId()), ct));

    /// <summary>Revoke all active tokens for a user â€” forces re-login on all devices.</summary>
    [HttpDelete("user/{userId:guid}")]
    [Authorize(Policy = PermCodes.Identity.Session.Revoke)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RevokeAllForUser(Guid userId, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new RevokeTokenCommand(TokenId: null, UserId: userId, GetActorId()), ct));

    private Guid GetActorId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
