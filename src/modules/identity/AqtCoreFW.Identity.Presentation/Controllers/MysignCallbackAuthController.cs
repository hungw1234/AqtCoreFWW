using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.SharedKernel.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Handles Viettel Mysign callback authentication requests (W6, async=3).
///
/// <para>Mysign calls this endpoint to obtain a short-lived inbound token before
/// delivering signing results. Flow: Mysign sends client credentials →
/// BE validates with constant-time compare → BE issues raw token → Mysign uses it as Bearer.</para>
///
/// <para>Route: <c>POST /api/signing/mysign-callback/auth</c></para>
/// <para>Auth: [AllowAnonymous] — Mysign presents credentials in the request body (OAuth2-style).</para>
/// <para>Thin controller: the not-configured guard, constant-time credential compare, and token
/// issuance live behind <see cref="IMysignInboundTokenService.AuthenticateCallbackAsync"/>.</para>
/// </summary>
[Route("api/signing/mysign-callback")]
[AllowAnonymous]
public sealed class MysignCallbackAuthController(
    IMysignInboundTokenService tokenService,
    ILogger<MysignCallbackAuthController> logger) : ApiControllerBase
{
    /// <summary>
    /// POST /api/signing/mysign-callback/auth
    /// Validates Mysign client credentials and returns a short-lived inbound Bearer token.
    /// </summary>
    [HttpPost("auth")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(503)]
    public async Task<IActionResult> Auth(
        [FromBody] MysignCallbackAuthRequest req,
        CancellationToken ct = default)
    {
        var outcome = await tokenService
            .AuthenticateCallbackAsync(req.client_id, req.client_secret, ct)
            .ConfigureAwait(false);

        switch (outcome.Status)
        {
            case MysignCallbackAuthStatus.NotConfigured:
                return StatusCode(503, new { status_code = "01", message = "Callback inbound not configured" });

            case MysignCallbackAuthStatus.InvalidCredentials:
                logger.LogWarning(
                    "Mysign callback auth failed: bad credentials from {Ip}",
                    HttpContext.Connection.RemoteIpAddress);
                return Unauthorized(new { status_code = "01", message = "Invalid credentials" });

            default:
                logger.LogInformation(
                    "Mysign callback auth successful — token issued, expires_in={ExpiresIn}s, ip={Ip}",
                    outcome.ExpiresInSeconds, HttpContext.Connection.RemoteIpAddress);
                return Ok(new
                {
                    status_code = "00",
                    message     = "OK",
                    data = new
                    {
                        access_token = outcome.AccessToken,
                        expires_in   = outcome.ExpiresInSeconds,
                        consented_on = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    },
                });
        }
    }
}

/// <summary>Request body for <c>POST /api/signing/mysign-callback/auth</c>.</summary>
public sealed record MysignCallbackAuthRequest(
    string? grant_type,
    string? client_id,
    string? client_secret);
