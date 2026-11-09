using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// User-facing digital signature endpoints (QĐ 742 §5.1).
///
/// POST /sign       — server-side sign (VNPT SmartCA); rejects providers with ClientSideSigning capability.
/// POST /verify     — verify a CMS/PKCS#7 or Raw signature against a payload.
/// POST /verify-xml — verify an XMLDSig-signed document (W2 Tier-2); cert embedded in KeyInfo.
/// POST /verify-jws — verify a JWS compact serialization (W2 Tier-2); cert embedded in x5c header.
///
/// [RequiresMfa]: signing and verification are security-sensitive operations.
/// [EnableRateLimiting("write-ops")]: protect against brute-force verification attacks.
/// TenantId is resolved from JWT via ResolveTenantId() (base class helper, supports SystemAdmin override).
/// </summary>
[Route("api/v1/digital-signature")]
[Authorize]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class DigitalSignatureController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// POST /api/v1/digital-signature/sign
    /// Dispatches a server-side sign request to the tenant's configured provider.
    /// Returns 409 if the provider uses client-side signing (e.g. USB token — call the FE plugin instead).
    /// Returns 404 if the tenant has no signature config or signing is disabled.
    /// When IsPending=true in the response, poll v2/signatures/confirm with TransactionId.
    /// </summary>
    [HttpPost("sign")]
    [ProducesResponseType(typeof(SignPayloadResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Sign(
        [FromBody] SignRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId = ResolveUserId();

        var result = await mediator.Send(
            new SignPayloadCommand(
                TenantId: tenantId,
                Payload: request.Payload,
                UserId: userId.ToString(),
                Reason: request.Reason,
                ExtraParameters: request.ExtraParameters), ct);

        return ToApiResponse(result);
    }

    /// <summary>
    /// POST /api/v1/digital-signature/verify
    /// Verifies a digital signature against the given payload using the tenant's provider.
    ///
    /// <para><b>W1 — Raw format:</b> Set <c>format = "Raw"</c>, supply <c>signerCertBase64</c>
    /// (from VGCA <c>CertBase64</c> callback field), <c>hashAlg = "SHA256"</c>, <c>encode = "RSA"</c>.</para>
    /// <para><b>Legacy Pkcs7 format:</b> Omit new fields or set <c>format = "Pkcs7"</c> — behaves
    /// exactly as before W1 for all existing callers.</para>
    ///
    /// OcspMode defaults to the tenant's configured value; pass an override to enforce Hard mode.
    /// Returns 404 if the tenant has no signature config or signing is disabled.
    /// </summary>
    [HttpPost("verify")]
    [ProducesResponseType(typeof(VerifySignatureResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Verify(
        [FromBody] VerifyRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();

        var result = await mediator.Send(
            new VerifySignatureCommand(
                TenantId: tenantId,
                Payload: request.Payload,
                Signature: request.Signature,
                OcspModeOverride: request.OcspModeOverride,
                SignerCertBase64: request.SignerCertBase64,
                HashAlg: request.HashAlg,
                Encode: request.Encode,
                Format: request.Format), ct);

        return ToApiResponse(result);
    }

    /// <summary>
    /// POST /api/v1/digital-signature/verify-xml
    /// Verifies an XMLDSig-signed XML document (W2 Tier-2).
    /// The signing certificate must be embedded in the document's <c>KeyInfo/X509Data/X509Certificate</c> node.
    /// OcspMode defaults to the tenant's configured value; pass an override to enforce Hard mode.
    /// Returns 404 if the tenant has no signature config or signing is disabled.
    /// </summary>
    [HttpPost("verify-xml")]
    [ProducesResponseType(typeof(VerifySignatureResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> VerifyXml(
        [FromBody] VerifyXmlRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();

        var result = await mediator.Send(
            new VerifyXmlDsigCommand(
                TenantId: tenantId,
                XmlDsigBytes: request.XmlDsigBytes,
                OcspModeOverride: request.OcspModeOverride), ct);

        return ToApiResponse(result);
    }

    /// <summary>
    /// POST /api/v1/digital-signature/verify-jws
    /// Verifies a JWS compact serialization (W2 Tier-2).
    /// The signing certificate must be present in the <c>x5c</c> JWS header claim.
    /// <c>alg=none</c> is rejected unconditionally.
    /// OcspMode defaults to the tenant's configured value; pass an override to enforce Hard mode.
    /// Returns 404 if the tenant has no signature config or signing is disabled.
    /// </summary>
    [HttpPost("verify-jws")]
    [ProducesResponseType(typeof(VerifySignatureResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> VerifyJws(
        [FromBody] VerifyJwsRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();

        var result = await mediator.Send(
            new VerifyJwsCommand(
                TenantId: tenantId,
                JwsCompact: request.JwsCompact,
                OcspModeOverride: request.OcspModeOverride), ct);

        return ToApiResponse(result);
    }

    /// <summary>
    /// POST /api/v1/digital-signature/confirm
    /// Polls the outcome of a deferred async signing transaction (Viettel Mysign W5).
    ///
    /// <para>Call this after receiving <c>IsPending=true</c> from <c>/sign</c> with a <c>TransactionId</c>.
    /// Returns the same <see cref="SignPayloadResultDto"/> shape:</para>
    /// <list type="bullet">
    ///   <item>IsPending=true — transaction still in-flight; call again after a short delay.</item>
    ///   <item>IsSuccess=true, IsPending=false — signing completed; Signature populated.</item>
    ///   <item>IsSuccess=false — terminal failure; FailureReason describes the cause.</item>
    /// </list>
    ///
    /// Returns 404 if no signature config or signing is disabled.
    /// Returns 409 if the tenant's provider does not support async confirmation.
    /// </summary>
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(SignPayloadResultDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmSignatureRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var userId   = ResolveUserId();

        var result = await mediator.Send(
            new ConfirmSignatureCommand(
                TenantId:      tenantId,
                TransactionId: request.TransactionId,
                UserId:        userId.ToString()), ct);

        return ToApiResponse(result);
    }

    /// <summary>
    /// POST /api/v1/digital-signature/enrollments
    /// Enrols a signing certificate for a signer (forge-resistant binding key). SystemAdmin only.
    /// </summary>
    [HttpPost("enrollments")]
    [Authorize(Roles = Roles.SystemAdmin)]
    [ProducesResponseType(typeof(long), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> EnrollCert(
        [FromBody] EnrollSignerCertRequest request,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new EnrollSignerCertCommand(
                TenantId:     ResolveTenantId(),
                UserId:       request.UserId,
                SerialNumber: request.SerialNumber,
                Issuer:       request.Issuer,
                Thumbprint:   request.Thumbprint,
                Subject:      request.Subject,
                EnrolledBy:   ResolveUserId()), ct);

        return ToApiResponse(result);
    }

    /// <summary>
    /// GET /api/v1/digital-signature/enrollments/{userId}
    /// Lists a signer's certificate enrollments (active + revoked). SystemAdmin only.
    /// </summary>
    [HttpGet("enrollments/{userId:guid}")]
    [Authorize(Roles = Roles.SystemAdmin)]
    [ProducesResponseType(typeof(IReadOnlyList<SignerCertEnrollmentDto>), 200)]
    public async Task<IActionResult> ListEnrollments(
        [FromRoute] Guid userId,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new ListSignerCertEnrollmentsQuery(ResolveTenantId(), userId), ct);

        return ToApiResponse(result);
    }
}

// ── Request DTOs ────────────────────────────────────────────────────────────

public sealed record SignRequest(
    byte[] Payload,
    string? Reason,
    Dictionary<string, string>? ExtraParameters);

/// <summary>
/// POST body for /digital-signature/verify.
///
/// <para><b>W1 new fields (Raw format):</b>
/// <list type="bullet">
///   <item><term><see cref="SignerCertBase64"/></term>
///     <description>Base64-encoded DER certificate — the VGCA <c>CertBase64</c> callback field.
///     Required when <see cref="Format"/> is <c>Raw</c>; omit for Pkcs7.</description></item>
///   <item><term><see cref="HashAlg"/></term><description>Hash algorithm, e.g. "SHA256" (default).</description></item>
///   <item><term><see cref="Encode"/></term><description>Asymmetric algorithm, e.g. "RSA" (default) or "ECDSA".</description></item>
///   <item><term><see cref="Format"/></term>
///     <description>Signature layout: "Raw" (W1 Tier-1) or "Pkcs7" (legacy default).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed record VerifyRequest(
    byte[] Payload,
    byte[] Signature,
    OcspMode? OcspModeOverride,
    string? SignerCertBase64 = null,
    string HashAlg = "SHA256",
    string Encode = "RSA",
    SignatureFormat Format = SignatureFormat.Pkcs7);

/// <summary>
/// POST body for /digital-signature/verify-xml (W2 Tier-2).
/// <para><c>XmlDsigBytes</c>: base64-encoded bytes of the XMLDSig-signed XML document.
/// The signing certificate must be embedded in <c>KeyInfo/X509Data/X509Certificate</c>.</para>
/// </summary>
public sealed record VerifyXmlRequest(
    byte[] XmlDsigBytes,
    OcspMode? OcspModeOverride);

/// <summary>
/// POST body for /digital-signature/verify-jws (W2 Tier-2).
/// <para><c>JwsCompact</c>: JWS compact serialization string (<c>header.payload.signature</c>).
/// The signing certificate must be in the <c>x5c</c> header claim.
/// <c>alg=none</c> is rejected unconditionally.</para>
/// </summary>
public sealed record VerifyJwsRequest(
    string JwsCompact,
    OcspMode? OcspModeOverride);

/// <summary>
/// POST body for /digital-signature/confirm (W5 async-poll deferred).
/// <para>Supply the <c>TransactionId</c> returned by <c>/sign</c> when <c>IsPending=true</c>.</para>
/// </summary>
public sealed record ConfirmSignatureRequest(string TransactionId);

/// <summary>
/// POST body for /digital-signature/enrollments — enrol a signing certificate for a signer.
/// Serial+issuer (or thumbprint) become the forge-resistant binding key.
/// </summary>
public sealed record EnrollSignerCertRequest(
    Guid UserId,
    string SerialNumber,
    string Issuer,
    string Thumbprint,
    string? Subject = null);
