using AqtCoreFW.Identity.Application.Signing.Profiles;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Self-service signature-certificate enrollment for the authenticated user (two-phase possession proof).
/// Identity (TenantId + UserId) is resolved from the JWT server-side and never body-bound. Phase 1 issues
/// a challenge; phase 2 submits the signed challenge to enrol a <b>Pending</b> certificate (admin approves
/// later). <c>ResolveTenantId</c> throws (→ 401) when the tenant claim is missing (fail-closed).
/// </summary>
[Route("api/v1/me/signature-cert")]
[Authorize]
public sealed class UserSignatureEnrollmentController(ISender mediator) : ApiControllerBase
{
    /// <summary>Phase 1: issue a single-use possession-proof challenge for the caller to sign.</summary>
    [HttpPost("challenge")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> IssueChallenge(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(new IssueEnrollmentNonceCommand(tenantId, userId), ct);
        return ToApiResponse(result);
    }

    /// <summary>
    /// Phase 2: submit the signed challenge. For USB the body carries the raw signature + DER cert; for
    /// Remote the server signs via Mysign (phone OTP) and the body's signature/cert fields are unused.
    /// Returns the new enrollment's PublicId (or an existing active one — idempotent on same serial+issuer).
    /// </summary>
    [HttpPost("enroll")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Enroll(
        [FromBody] EnrollSignatureCertRequest request, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var result = await mediator.Send(new EnrollMySignatureCertCommand(
            TenantId:          tenantId,
            CallerUserId:      userId,
            Kind:              request.Kind,
            NonceId:           request.NonceId,
            SignedNonceBase64: request.SignedNonceBase64,
            CertBase64:        request.CertBase64), ct);
        return ToApiResponse(result);
    }
}

/// <summary>
/// Request body for enrollment submission. Identity comes from the JWT (not here); <see cref="Kind"/> is a
/// flow selector and <c>ApprovalStatus</c> is never client-supplied (always Pending server-side).
/// </summary>
public sealed record EnrollSignatureCertRequest(
    EnrollmentKind Kind,
    Guid NonceId,
    string? SignedNonceBase64,
    string? CertBase64);
