using AqtCoreFW.Identity.Application.Queries.GetUserEffectivePermissions;
using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Self-service identity endpoints — current user metadata used by FE for UI gating.
/// Replaces JWT-stuffed permission claims (token bloat) with on-demand API calls.
/// </summary>
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// Returns the current user's effective roles + permission codes (Redis-cached TTL 10min).
    /// Consumed by FE useMyEffectivePermissions() for menu/route/button gating.
    /// </summary>
    [HttpGet("permissions")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetMyPermissions(CancellationToken ct = default)
    {
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(new GetUserEffectivePermissionsQuery(userId), ct);

        // Use RoleName (PascalCase) to match the JWT `role` claim format —
        // FE guards (e.g. AdminGuard) check `roles.includes('SystemAdmin')`.
        // RBAC-3 v2.133p8: union direct + group-inherited roles so users whose only
        // access path is group membership are not treated as role-less by the FE.
        return ToApiResponse(result.Map(s => new MyPermissionsResponse(
            s.UserId,
            s.DirectRoles.Select(r => r.RoleName)
                .Concat(s.GroupRoles.Select(gr => gr.RoleName))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList(),
            s.PermissionCodes.ToList())));
    }

    /// <summary>
    /// Self-service: sets/updates the caller's own signing national id (CCCD) — the prerequisite for
    /// enrolling a signing certificate. Identity is taken from the JWT, never the body. Refused (400,
    /// SIGNING_NATIONAL_ID_LOCKED_BY_ENROLLMENT) when the caller already has an active enrollment.
    /// 422 when the value is not exactly 12 digits.
    /// </summary>
    [HttpPut("signing-national-id")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SetSigningNationalId(
        [FromBody] SetSigningNationalIdRequest request, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(
            new SetMySigningNationalIdCommand(tenantId, userId, request.NationalId), ct);
        return ToApiResponse(result);
    }
}

/// <summary>DTO for GET /api/v1/me/permissions.</summary>
public sealed record MyPermissionsResponse(
    Guid UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> PermissionCodes);

/// <summary>Request body for PUT /api/v1/me/signing-national-id (identity comes from the JWT, not the body).</summary>
public sealed record SetSigningNationalIdRequest(string NationalId);
