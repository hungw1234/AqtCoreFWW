using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Handles Viettel Mysign callback result deliveries (W6, async=3).
///
/// <para>After signing completes on mobile, Mysign POSTs the result here using the
/// inbound Bearer token obtained from <see cref="MysignCallbackAuthController.Auth"/>.</para>
///
/// <para>Route: <c>POST /api/signing/mysign-callback/result</c></para>
/// <para>Auth: [AllowAnonymous] — token validated inline via <see cref="IMysignInboundTokenService"/>
/// before dispatch. The validation MUST stay inline (not an authorization filter): [AllowAnonymous]
/// disables auth filters, so a filter would silently never run.</para>
/// <para>Thin controller: completion logic + persistence + audit live in
/// <see cref="CompleteMysignCallbackCommand"/>'s handler.</para>
/// </summary>
[Route("api/signing/mysign-callback")]
[AllowAnonymous]
public sealed class MysignCallbackResultController(
    IMysignInboundTokenService tokenService,
    ISender mediator,
    ILogger<MysignCallbackResultController> logger) : ApiControllerBase
{
    /// <summary>
    /// POST /api/signing/mysign-callback/result
    /// Receives the signing result from Viettel Mysign and updates the pending transaction.
    /// </summary>
    [HttpPost("result")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Result(
        [FromBody] MysignCallbackResultRequest req,
        CancellationToken ct = default)
    {
        // ── 1. Extract and validate inbound Bearer token (inline — see class remarks) ──
        string? token = null;
        var authHeader = Request.Headers.Authorization.ToString();
        if (authHeader.Length > 7 &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrEmpty(token) ||
            !await tokenService.TryValidateAsync(token, ct).ConfigureAwait(false))
        {
            logger.LogWarning(
                "Mysign callback result: invalid or missing Bearer token from {Ip}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { status_code = "01", message = "Invalid token" });
        }

        // ── 2. Validate transactionId presence ────────────────────────────────
        var txId = req.meta_data?.transaction_id;
        if (string.IsNullOrEmpty(txId))
        {
            // No txId in body — ack silently (Mysign may send test pings)
            logger.LogInformation(
                "Mysign callback result: missing transaction_id — silent ack, ip={Ip}",
                HttpContext.Connection.RemoteIpAddress);
            return Ok(new { status_code = "00", message = "Missing transaction_id (idempotent ack)" });
        }

        // ── 3. Dispatch the audited completion command ─────────────────────────
        var signature = req.data?.results is { Length: > 0 } ? req.data.results[0].signature : null;
        var result = await mediator
            .Send(new CompleteMysignCallbackCommand(txId, req.meta_data?.status, signature), ct)
            .ConfigureAwait(false);

        // The command is idempotent: a re-delivery / unknown txId is a 200 ack, never a 4xx — preserve
        // the existing "silent OK" contract that avoids disclosing internal state.
        var message = result.IsSuccess && result.Value.Transitioned ? "Thành công" : "Already processed";
        return Ok(new { status_code = "00", message });
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>Top-level body for <c>POST /api/signing/mysign-callback/result</c>.</summary>
public sealed record MysignCallbackResultRequest(
    MysignCallbackMetaData? meta_data,
    MysignCallbackData? data);

/// <summary>Metadata block — carries transactionId and status code from Viettel.</summary>
public sealed record MysignCallbackMetaData(
    string? request_id,
    string? transaction_id,
    string? description,
    string? status);

/// <summary>Data block containing the list of signed results.</summary>
public sealed record MysignCallbackData(
    long? action_timestamp,
    long? signature_timestamp,
    MysignCallbackResult[]? results);

/// <summary>Individual document result — document_id + base64-encoded signature.</summary>
public sealed record MysignCallbackResult(
    string? document_id,
    string? signature);
