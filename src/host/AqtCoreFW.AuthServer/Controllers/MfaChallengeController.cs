using System.Security.Claims;
using AqtCoreFW.Audit.Domain.Services;
using AqtCoreFW.AuthServer.Services;
using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AqtCoreFW.AuthServer.Controllers;

/// <summary>
/// POST /connect/mfa-challenge
/// Validates a single-use temp_token + TOTP code (or recovery code), then issues a full
/// access token with mfa_at claim.
///
/// This endpoint is in AuthServer (not Api) because OpenIddict SignIn requires the host
/// that registered AddServer(). Attempting SignIn in the Api host throws at runtime.
///
/// Security notes:
///   - temp_token is single-use: MfaTempTokenService marks tokenId consumed on first Validate call.
///   - mfa_at is emitted as Unix epoch seconds into AccessToken only.
///   - No [Authorize] — the temp_token IS the authentication proof for this step.
///   - Rate-limited via "mfa-verify" policy (registered in Api; AuthServer has no RL middleware
///     yet — acceptable since this endpoint is behind HTTPS + temp_token + TOTP entropy).
/// </summary>
[Route("/connect/mfa-challenge")]
public sealed class MfaChallengeController(
    UserManager<ApplicationUser> userManager,
    MfaTempTokenService mfaTempTokenService,
    IMfaService mfaService,
    IRecoveryCodeService recoveryCodeService,
    IAuditService auditService) : Controller
{
    private const string MfaAtClaimType = "mfa_at";

    /// <summary>
    /// POST /connect/mfa-challenge
    /// Body (JSON): { "temp_token": "...", "totp_code": "123456" }
    ///   OR         { "temp_token": "...", "recovery_code": "ABCD1234" }
    /// On success: issues full OpenIddict access token + mfa_at claim.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Challenge([FromBody] MfaChallengeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TempToken))
            return BadRequest(new { error = "invalid_request", error_description = "temp_token is required." });

        // Validate single-use temp_token → extract userId.
        var userId = mfaTempTokenService.Validate(request.TempToken);
        if (userId is null)
            return BadRequest(new { error = "invalid_grant", error_description = "temp_token is invalid, expired, or already used." });

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null)
            return BadRequest(new { error = "invalid_grant", error_description = "User not found." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = Request.Headers.UserAgent.FirstOrDefault();

        // Validate TOTP code or recovery code — exactly one must be provided.
        bool verified = false;
        string method;

        if (!string.IsNullOrWhiteSpace(request.TotpCode))
        {
            verified = await mfaService.ValidateTotpAsync(userId.Value, request.TotpCode);
            method = "totp";
        }
        else if (!string.IsNullOrWhiteSpace(request.RecoveryCode))
        {
            verified = await recoveryCodeService.ValidateAndConsumeAsync(
                userId.Value, request.RecoveryCode, HttpContext.RequestAborted);
            method = "recovery_code";
        }
        else
        {
            return BadRequest(new { error = "invalid_request", error_description = "Either totp_code or recovery_code is required." });
        }

        if (!verified)
        {
            await auditService.LogLoginAttemptAsync(
                user.Id, user.Email ?? "", ip, ua, false, $"MFA challenge failed (method={method})");
            return BadRequest(new { error = "invalid_grant", error_description = "MFA code is invalid." });
        }

        await auditService.LogLoginAttemptAsync(
            user.Id, user.Email ?? "", ip, ua, true, $"MFA challenge success (method={method})");

        // Build identity with mfa_at claim for the access token.
        var identity = await CreateIdentityAsync(user);
        var mfaAtValue = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var mfaAtClaim = new Claim(MfaAtClaimType, mfaAtValue, ClaimValueTypes.Integer64);
        mfaAtClaim.SetDestinations(Destinations.AccessToken);
        identity.AddClaim(mfaAtClaim);

        identity.SetScopes("openid", "profile", "email", "roles", "api", "offline_access");
        identity.SetResources("aqtcorefw-api-resource");

        return SignIn(new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<ClaimsIdentity> CreateIdentityAsync(ApplicationUser user)
    {
        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        identity.AddClaim(Claims.Subject, user.Id.ToString());
        identity.AddClaim(Claims.Email, user.Email ?? "");
        identity.AddClaim(Claims.Name, user.FullName);
        identity.AddClaim(Claims.PreferredUsername, user.UserName ?? user.Email ?? "");

        var roles = await userManager.GetRolesAsync(user);
        foreach (var role in roles)
            identity.AddClaim(Claims.Role, role);

        if (user.TenantId.HasValue)
            identity.AddClaim("tenant_id", user.TenantId.Value.ToString());

        foreach (var claim in identity.Claims)
        {
            if (claim.Type == MfaAtClaimType) continue; // destination set explicitly above
            claim.SetDestinations(claim.Type switch
            {
                Claims.Name or Claims.PreferredUsername =>
                    [Destinations.AccessToken, Destinations.IdentityToken],
                Claims.Email => [Destinations.IdentityToken],
                Claims.Role => [Destinations.AccessToken, Destinations.IdentityToken],
                "tenant_id" => [Destinations.AccessToken, Destinations.IdentityToken],
                _ => [Destinations.AccessToken]
            });
        }

        return identity;
    }
}

/// <summary>Request body for POST /connect/mfa-challenge.</summary>
public sealed record MfaChallengeRequest(
    string TempToken,
    string? TotpCode = null,
    string? RecoveryCode = null);
