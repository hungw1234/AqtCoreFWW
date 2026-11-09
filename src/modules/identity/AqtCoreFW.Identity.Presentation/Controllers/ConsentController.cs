using AqtCoreFW.Identity.Application.Commands.GrantConsent;
using AqtCoreFW.Identity.Application.Commands.WithdrawConsent;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// PDPL Law 91/2025 Art. 11 — Consent management endpoints.
/// Authenticated users can view, grant, and withdraw their own data processing consent records.
/// </summary>
[Route("api/v1/identity/consents")]
[Authorize]
public sealed class ConsentController(ISender mediator, IConsentRepository consents) : ApiControllerBase
{
    /// <summary>List all consent records for the authenticated user.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetMyConsents(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var records = await consents.GetByUserIdAsync(userId, ct);
        return Ok(ApiResponse<object>.Ok(new { items = records }));
    }

    /// <summary>Grant a new data processing consent record (PDPL Art. 11).</summary>
    [HttpPost("grant")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Grant(
        [FromBody] GrantConsentRequest request,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var tenantId = GetTenantId();

        return ToApiResponse(await mediator.Send(
            new GrantConsentCommand(
                userId,
                tenantId,
                request.Purpose,
                request.Scope ?? "general",
                "citizen"),
            ct));
    }

    /// <summary>Withdraw a previously granted consent record (PDPL Art. 11 — right to object).</summary>
    [HttpPost("withdraw")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Withdraw(
        [FromBody] WithdrawConsentRequest request,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        // Look up active consent by purpose for this user
        var record = await consents.GetByUserAndPurposeAsync(userId, request.Purpose, ct);
        if (record is null)
            return ToApiResponse(Result.Fail(new NotFoundError("No active consent found for the specified purpose.")));

        var result = await mediator.Send(
            new WithdrawConsentCommand(record.PublicId, userId, request.Scope ?? "user withdrawal"), ct);
        if (result.IsFailed) return ToApiResponse(result);
        return Ok(ApiResponse<object>.Ok(new { withdrawn = true, consentId = record.PublicId }));
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private Guid GetTenantId()
    {
        var tid = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(tid, out var id) ? id : Guid.Empty;
    }
}

/// <summary>Request body for granting consent.</summary>
public sealed record GrantConsentRequest(string Purpose, string? Scope = null, string? GrantedAt = null);

/// <summary>Request body for withdrawing consent.</summary>
public sealed record WithdrawConsentRequest(string Purpose, string? Scope = null);
