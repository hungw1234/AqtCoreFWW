using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Signing;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Default <see cref="ISignerBindingGuard"/> — two-tier binding strategy, fail-closed. Never throws
/// for a binding failure (returns a verdict).
///
/// <para><b>Enrolled tier (default):</b> any signer who has at least one enrollment row (regardless of
/// status) is governed exclusively by the enrolled tier. Binding succeeds only when an
/// <see cref="CertApprovalStatus.Approved"/>, active enrollment exists whose serial+issuer or thumbprint
/// matches the presented certificate. This tier is cert-type-agnostic — it handles both personal and
/// organisation certificates identically (the binding key is serial/issuer/thumbprint, not the subject DN).</para>
///
/// <para><b>Bootstrap tier (Mysign / personal-legacy fallback — fires ONLY when the signer has zero
/// enrollment rows):</b> for signers who have never enrolled any certificate (typically early Mysign users
/// whose personal cert was provisioned out-of-band), binding is attempted via the CCCD/CMND national-id
/// RDN in the cert subject against the signer's stored <c>SigningNationalId</c>. This tier is intentionally
/// restricted to zero-enrollment signers: a revoked, rejected, or pending enrollment row keeps the signer
/// in the enrolled tier, preventing a downgrade to the weaker CCCD-subject check. Organisation certificates
/// — which carry no national-id RDN — can never satisfy this bootstrap and MUST go through enrollment.</para>
/// </summary>
public sealed class SignerBindingGuard(
    ISignerCertEnrollmentRepository enrollments,
    UserManager<ApplicationUser> userManager) : ISignerBindingGuard
{
    private static readonly StringComparison Ci = StringComparison.OrdinalIgnoreCase;

    public async Task<SignerBindingVerdict> VerifyAsync(
        CertificateInfo signingCert,
        Guid signerUserId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (signingCert is null)
            return SignerBindingVerdict.NotBound("no signing certificate to bind (fail-closed)");
        if (signerUserId == Guid.Empty || tenantId == Guid.Empty)
            return SignerBindingVerdict.NotBound("missing signer/tenant context (fail-closed)");

        // 1. Tier decision by ANY enrollment row (all-status — H1). A user with even a single enrollment
        // row (Pending/Approved/Rejected/revoked) is governed by the enrolled tier; we must NOT silently
        // fall back to the weaker CCCD bootstrap just because no row is currently active+Approved (that
        // would let a revoked/rejected signer downgrade to the bootstrap and keep signing).
        if (await enrollments.HasAnyEnrollmentAsync(tenantId, signerUserId, ct))
        {
            // Enrolled-tier match: forge-resistant key (serial+issuer or thumbprint) on an APPROVED, active
            // enrollment only. Pending (not yet admin-approved) and Rejected/revoked rows do NOT bind.
            var active = await enrollments.GetActiveByUserAsync(tenantId, signerUserId, ct);
            var match = active.Any(e =>
                e.ApprovalStatus == CertApprovalStatus.Approved
                && ((!string.IsNullOrEmpty(signingCert.Thumbprint)
                        && string.Equals(e.Thumbprint, signingCert.Thumbprint, Ci))
                    || (string.Equals(e.SerialNumber, signingCert.SerialNumber, Ci)
                        && string.Equals(e.Issuer, signingCert.Issuer, Ci))));

            return match
                ? SignerBindingVerdict.Bound("approved enrolled serial/issuer or thumbprint match")
                : SignerBindingVerdict.NotBound(
                    "signing certificate has no approved enrollment for the assigned signer");
        }

        // 2. Bootstrap — Mysign/personal-legacy fallback (ONLY when the signer has zero enrollment rows).
        // Extracts the CCCD/CMND national-id RDN from the cert subject and compares it to the signer's
        // stored SigningNationalId. This path exists solely for early Mysign users whose personal cert was
        // provisioned out-of-band before the enrollment workflow existed. Organisation certificates (no
        // national-id RDN in subject) will always fail this bootstrap — they MUST be enrolled.
        // ApplicationUser is cross-tenant resolvable (no tenant query filter / RLS on AspNetUsers), so
        // assert the loaded signer actually belongs to this tenant before trusting its national id —
        // the enrolled-key path above is already tenant-scoped; this closes the same gap on the bootstrap.
        var user = await userManager.FindByIdAsync(signerUserId.ToString());
        if (user is null || user.TenantId != tenantId)
            return SignerBindingVerdict.NotBound(
                "signer not found in this tenant — cannot bind (fail-closed)");

        var expected = VgcaCertSubjectParser.NormalizeNationalId(user.SigningNationalId);
        if (string.IsNullOrEmpty(expected))
            return SignerBindingVerdict.NotBound(
                "signer has no enrolled certificate and no SigningNationalId — cannot bind (fail-closed)");

        if (!VgcaCertSubjectParser.TryExtractNationalId(signingCert.Subject, out var certNationalId))
            return SignerBindingVerdict.NotBound(
                "could not extract a national id from the signing certificate subject (fail-closed)");

        return string.Equals(certNationalId, expected, StringComparison.Ordinal)
            ? SignerBindingVerdict.Bound("cert subject national id matches signer SigningNationalId")
            : SignerBindingVerdict.NotBound(
                "signing certificate national id does not match the assigned signer");
    }
}
