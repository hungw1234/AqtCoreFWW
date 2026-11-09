using AqtCoreFW.Identity.Application.Commands.ChangePassword;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Self-service account endpoints — any authenticated user.
/// No admin role required.
/// </summary>
[Route("api/v1/account")]
[Authorize]
public sealed class AccountController(ISender mediator) : ApiControllerBase
{
    /// <summary>Change the current user's password (requires current password verification).</summary>
    [HttpPost("change-password")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        return ToApiResponse(await mediator.Send(
            new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword), ct));
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

/// <summary>Request body for self-service password change.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
