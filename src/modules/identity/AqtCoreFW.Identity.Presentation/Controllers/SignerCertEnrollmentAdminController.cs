using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin review + approval of self-service signing-certificate enrollments. Gated by the dedicated
/// least-privilege permission <see cref="PermCodes.Signature.Enrollment.Approve"/> — NOT SystemAdmin
/// role, and NOT the far broader SYSTEM.CONFIG.MANAGE it previously reused (which forced granting
/// full system-config admin just to approve a certificate). The tenant is read STRICTLY from the JWT
/// claim via <see cref="ITenantContext.TenantId"/>, deliberately bypassing
/// <see cref="ApiControllerBase.ResolveTenantId"/> which honours the SystemAdmin X-Tenant-Id override
/// (that override would allow cross-tenant approval — H4). Enrollments are addressed by PublicId (M1).
/// </summary>
[Route("api/v1/admin/signer-cert-enrollments")]
[Authorize(Policy = PermCodes.Signature.Enrollment.Approve)]
[RequiresMfa]
public sealed class SignerCertEnrollmentAdminController(ISender mediator, ITenantContext tenantContext)
    : ApiControllerBase
{
    // H4: tenant comes from the JWT claim only — never the X-Tenant-Id header. Fail-closed if absent.
    private Guid TenantId => tenantContext.TenantId
        ?? throw new UnauthorizedAccessException("Missing tenant claim");

    /// <summary>Lists the caller-tenant's Pending enrollment review queue (oldest-first, paginated).</summary>
    [HttpGet("pending")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ListPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(
            new ListPendingEnrollmentsQuery(TenantId, page, pageSize), ct));

    /// <summary>Approves a Pending enrollment (by PublicId) so its certificate can sign.</summary>
    [HttpPost("{publicId:guid}/approve")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Approve(Guid publicId, CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(
            new ApproveSignerCertEnrollmentCommand(TenantId, ResolveUserId(), publicId), ct));

    /// <summary>Rejects a Pending enrollment (by PublicId) with a reason; re-enroll stays possible (H3).</summary>
    [HttpPost("{publicId:guid}/reject")]
    [ProducesResponseType(204)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Reject(
        Guid publicId, [FromBody] RejectEnrollmentRequest request, CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(
            new RejectSignerCertEnrollmentCommand(TenantId, ResolveUserId(), publicId, request.Reason), ct));
}

/// <summary>Request body for rejecting an enrollment (admin identity + tenant come from the JWT).</summary>
public sealed record RejectEnrollmentRequest(string Reason);
