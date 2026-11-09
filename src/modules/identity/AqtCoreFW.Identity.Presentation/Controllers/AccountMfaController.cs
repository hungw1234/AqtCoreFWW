using AqtCoreFW.Identity.Application.Mfa;
using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// TOTP enrolment + recovery code endpoints for the current authenticated user.
/// Route family: /api/v1/account/mfa/* (Option B — replaces /api/v1/mfa/*).
///
/// Allowlist (no [RequiresMfa]):
///   GET  enrol/totp     — start enrolment (user may not have MFA yet)
///   POST enrol/totp     — complete enrolment (user is proving possession of new secret)
/// Protected (requires fresh MFA session, 8 h window):
///   DELETE totp                — unenrol (wipe TotpSecret + codes)
///   POST   recovery-codes/regenerate — rotate recovery codes
///
/// Thin controller: all state mutations dispatch an audited <c>ICommand</c> (AuditBehavior +
/// validation); reads go through <see cref="IMfaService"/>. No DbContext / UserManager is touched here
/// (the prior service-locator UserManager writes were unaudited — ADR-004 gap, now closed).
/// </summary>
[Route("api/v1/account/mfa")]
[Authorize]
[EnableRateLimiting("write-ops")]
public sealed class AccountMfaController(
    ISender mediator,
    IMfaService mfaService) : ApiControllerBase
{
    // ── Enrolment ────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/account/mfa/status
    /// Returns whether the current user has completed TOTP enrolment.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<MfaEnrolmentStatus>), 200)]
    public async Task<IActionResult> GetStatus(CancellationToken ct = default)
    {
        var status = await mfaService.GetEnrolmentStatusAsync(ResolveUserId());
        if (status is null) return Unauthorized();

        return Ok(ApiResponse<MfaEnrolmentStatus>.Ok(status));
    }

    /// <summary>
    /// GET /api/v1/account/mfa/enrol/totp
    /// Starts TOTP enrolment: generates pending secret (10-min TTL in IMemoryCache),
    /// returns otpauth:// URI for QR rendering and plain base32 for manual entry.
    /// Not protected by [RequiresMfa] — user may be starting enrolment from scratch.
    /// </summary>
    [HttpGet("enrol/totp")]
    [ProducesResponseType(typeof(ApiResponse<BeginTotpEnrolResponse>), 200)]
    public async Task<IActionResult> BeginEnrol(CancellationToken ct = default)
    {
        var userId = ResolveUserId();
        var (qrUri, secret) = await mfaService.BeginTotpEnrolAsync(userId);
        return Ok(ApiResponse<BeginTotpEnrolResponse>.Ok(
            new BeginTotpEnrolResponse(qrUri, secret)));
    }

    /// <summary>
    /// POST /api/v1/account/mfa/enrol/totp
    /// Completes TOTP enrolment: validates TOTP code against cached pending secret,
    /// persists TotpSecret + TotpEnrolledAt, returns one-time plaintext recovery codes.
    /// Recovery codes shown ONCE — client must prompt user to save them.
    /// Not protected by [RequiresMfa] — user is completing their first enrolment.
    /// </summary>
    [HttpPost("enrol/totp")]
    [EnableRateLimiting("mfa-verify")]
    [ProducesResponseType(typeof(ApiResponse<CompleteTotpEnrolResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> CompleteEnrol(
        [FromBody] CompleteTotpEnrolRequest request,
        CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(
            new CompleteTotpEnrolCommand(ResolveUserId(), request.TotpCode), ct));

    // ── Protected actions (require fresh MFA) ────────────────────────────────────

    /// <summary>
    /// DELETE /api/v1/account/mfa/totp
    /// Unenrols TOTP: clears TotpSecret, TotpEnrolledAt, RecoveryCodesHash.
    /// Requires [RequiresMfa] — user must have completed MFA within 8 h.
    /// </summary>
    [HttpDelete("totp")]
    [RequiresMfa]
    [ProducesResponseType(204)]
    public async Task<IActionResult> UnenrolTotp(CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new UnenrolTotpCommand(ResolveUserId()), ct));

    /// <summary>
    /// POST /api/v1/account/mfa/recovery-codes/regenerate
    /// Invalidates all existing recovery codes and issues 16 fresh ones.
    /// Returns plaintext codes ONCE — client must prompt user to save them.
    /// Requires [RequiresMfa] — sensitive operation that replaces backup factor.
    /// </summary>
    [HttpPost("recovery-codes/regenerate")]
    [RequiresMfa]
    [ProducesResponseType(typeof(ApiResponse<RegenerateRecoveryCodesResult>), 200)]
    public async Task<IActionResult> RegenerateRecoveryCodes(CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new RegenerateRecoveryCodesCommand(ResolveUserId()), ct));
}

// ── Request/Response DTOs ─────────────────────────────────────────────────────

public sealed record BeginTotpEnrolResponse(string QrCodeUri, string SecretBase32);
public sealed record CompleteTotpEnrolRequest(string TotpCode);
