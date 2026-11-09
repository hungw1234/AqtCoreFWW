using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.MfaPolicies;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// Admin CRUD for per-role MFA policies (QÄ742 1.6.a).
/// Class-level [RequiresMfa] â€” all MFA policy management requires a fresh MFA session (8 h).
/// </summary>
[Route("api/v1/admin/security/mfa-policies")]
[Authorize(Policy = PermCodes.Security.MfaPolicy.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class MfaPoliciesAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>GET /api/v1/admin/security/mfa-policies â€” list all policies ordered by role.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MfaPolicyDto>>), 200)]
    public async Task<IActionResult> List(CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new GetMfaPoliciesQuery(), ct));

    /// <summary>
    /// POST /api/v1/admin/security/mfa-policies — create a new per-role policy.
    /// Returns 201 Created with the new policy PublicId (Guid).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Security.MfaPolicy.Manage)]
    [ProducesResponseType(typeof(ApiResponse<Guid>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 409)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMfaPolicyRequest request,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new CreateMfaPolicyCommand(
                request.Role,
                request.IsMandatory,
                request.GraceDays,
                request.AllowedFactors), ct);

        if (result.IsFailed)
            return ToApiResponse(result);

        return StatusCode(201, ApiResponse<Guid>.Ok(result.Value));
    }

    /// <summary>
    /// PUT /api/v1/admin/security/mfa-policies/{publicId} — update an existing policy by Guid.
    /// </summary>
    [HttpPut("{publicId:guid}")]
    [Authorize(Policy = PermCodes.Security.MfaPolicy.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Update(
        Guid publicId,
        [FromBody] UpdateMfaPolicyRequest request,
        CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(
            new UpdateMfaPolicyCommand(
                publicId,
                request.IsMandatory,
                request.GraceDays,
                request.AllowedFactors), ct));
}

// â”€â”€ Request DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record CreateMfaPolicyRequest(
    string Role,
    bool IsMandatory,
    int GraceDays,
    Identity.Domain.Entities.MfaFactor AllowedFactors);

public sealed record UpdateMfaPolicyRequest(
    bool IsMandatory,
    int GraceDays,
    Identity.Domain.Entities.MfaFactor AllowedFactors);
