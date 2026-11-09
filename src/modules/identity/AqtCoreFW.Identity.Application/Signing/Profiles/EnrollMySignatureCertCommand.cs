using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Signing;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Phase 2 of self-service enrollment: submits the signed possession-proof challenge to enrol a signing
/// certificate as <b>Pending</b> (admin approves later — P3). Caller identity is JWT-injected (never
/// body-bound). The flow forks on <see cref="EnrollmentKind"/>:
/// <list type="bullet">
///   <item><b>Remote (Viettel Mysign):</b> the server signs the nonce via Mysign sync mode (blocks for the
///     signer's phone OTP — that OTP is the possession proof) and reads the cert from the sync result.</item>
///   <item><b>Usb (VGCA):</b> the client signs the nonce with the USB plugin and submits the raw signature +
///     DER cert; possession is verified through the existing Raw-verify path.</item>
/// </list>
/// Both forks persist a Pending enrollment. Remote additionally requires an established
/// <see cref="ApplicationUser.SigningNationalId"/> and re-asserts cert-CCCD equality; USB accepts
/// org certs (no CCCD) and hard-rejects Personal certs whose CCCD mismatches the user's.
/// <c>ApprovalStatus</c> is a hardcoded literal — this handler can never emit Approved.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record EnrollMySignatureCertCommand(
    Guid TenantId,
    Guid CallerUserId,
    EnrollmentKind Kind,
    Guid NonceId,
    string? SignedNonceBase64,
    string? CertBase64) : ICommand<Guid>;

public sealed class EnrollMySignatureCertCommandValidator : AbstractValidator<EnrollMySignatureCertCommand>
{
    public EnrollMySignatureCertCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
        RuleFor(x => x.NonceId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
        // USB submits the signature + cert in-band; Remote obtains both server-side via Mysign.
        When(x => x.Kind == EnrollmentKind.UsbTokenVgca, () =>
        {
            RuleFor(x => x.SignedNonceBase64).NotEmpty();
            RuleFor(x => x.CertBase64).NotEmpty();
        });
    }
}

public sealed class EnrollMySignatureCertCommandHandler(
    ISignerEnrollmentNonceRepository nonces,
    ISignerCertEnrollmentRepository enrollments,
    ISender mediator,
    UserManager<ApplicationUser> userManager,
    IGuidGenerator guidGen)
    : IRequestHandler<EnrollMySignatureCertCommand, Result<Guid>>
{
    // Caps the per-user admin-approval queue (anti-DoS — a user cannot flood pending requests).
    private const int MaxPendingEnrollments = 5;

    public async Task<Result<Guid>> Handle(EnrollMySignatureCertCommand cmd, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(cmd.CallerUserId.ToString());
        if (user is null || user.TenantId != cmd.TenantId)
            return Result.Fail(new NotFoundError($"User {cmd.CallerUserId} not found."));

        // Remote Mysign: CCCD must already be established (it is the Mysign account key and the
        // re-assert below compares the cert's CCCD against it). USB may proceed without a user CCCD —
        // the cert may be an org cert (no CCCD) or a personal cert whose CCCD is validated
        // post-possession-proof. Setting CCCD from this handler would make the re-assert circular (C1).
        if (cmd.Kind == EnrollmentKind.RemoteViettelMysign
            && string.IsNullOrWhiteSpace(user.SigningNationalId))
            return Result.Fail(new BadRequestError(
                "Signing national id must be established before enrolling a certificate.",
                "SIGNING_NATIONAL_ID_NOT_ESTABLISHED"));

        // ── Resolve + validate the possession-proof nonce ───────────────────────────────────────────
        var nonce = await nonces.GetAsync(cmd.TenantId, cmd.NonceId, ct);
        // Same generic code for "missing" and "belongs to another user" — no existence leak, anti-replay (C4).
        if (nonce is null || nonce.UserId != cmd.CallerUserId)
            return Result.Fail(new BadRequestError("Enrollment challenge is invalid.", "ENROLLMENT_NONCE_INVALID"));
        if (nonce.IsConsumed)
            return Result.Fail(new BadRequestError("Enrollment challenge already used.", "ENROLLMENT_NONCE_CONSUMED"));

        var now = DateTime.UtcNow;
        if (nonce.IsExpired(now))
            return Result.Fail(new BadRequestError("Enrollment challenge expired.", "ENROLLMENT_NONCE_EXPIRED"));

        // Consume FIRST (single flush) so a failed/abandoned attempt can never be replayed — the user
        // simply re-requests a challenge. Anti-replay takes priority over "don't burn the nonce on error".
        // RowVersion serialises a concurrent double-submit: the DB write picks one winner; the loser's
        // optimistic-concurrency failure is surfaced as the same clean "already used" error as the
        // in-memory check (mirrors the sibling SigningDocFlowNonce consume path).
        nonce.Consume(now);
        try
        {
            await nonces.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(new BadRequestError("Enrollment challenge already used.", "ENROLLMENT_NONCE_CONSUMED"));
        }

        // ── Possession proof per provider kind ──────────────────────────────────────────────────────
        var certResult = cmd.Kind switch
        {
            EnrollmentKind.UsbTokenVgca           => await ProveUsbPossessionAsync(cmd, nonce.Challenge, ct),
            EnrollmentKind.RemoteViettelMysign    => await ProveRemotePossessionAsync(cmd, nonce.Challenge, ct),
            _ => Result.Fail<CertificateInfo>(new BadRequestError(
                "Unsupported enrollment kind.", "ENROLLMENT_KIND_UNSUPPORTED")),
        };
        if (certResult.IsFailed)
            return certResult.ToResult();

        var cert = certResult.Value;

        // ── Provider-scoped identity assertion + subject classification ──────────────────────────────
        // Remote: cert MUST carry a CCCD AND match the user's established SigningNationalId (unchanged).
        // USB:    classify the subject; org cert (no CCCD in subject) → accept; Personal + cert-CCCD
        //         present + user-CCCD present + mismatch → HARD REJECT (RT-1 #1 decision table).
        //         Personal + user has no CCCD → accept (nothing to contradict yet).
        CertSubjectType subjectType;
        string displayName;

        if (cmd.Kind == EnrollmentKind.RemoteViettelMysign)
        {
            if (!VgcaCertSubjectParser.TryExtractNationalId(cert.Subject, out var remoteCertNationalId))
                return Result.Fail(new BadRequestError(
                    "Certificate subject does not carry a national id.", "ENROLLMENT_CERT_NO_NATIONAL_ID"));
            if (!string.Equals(
                    VgcaCertSubjectParser.NormalizeNationalId(remoteCertNationalId),
                    VgcaCertSubjectParser.NormalizeNationalId(user.SigningNationalId),
                    StringComparison.Ordinal))
                return Result.Fail(new BadRequestError(
                    "Certificate does not belong to this user.", "ENROLLMENT_CERT_NATIONAL_ID_MISMATCH"));
            (subjectType, displayName) = VgcaCertSubjectParser.Classify(cert.Subject);
        }
        else // EnrollmentKind.UsbTokenVgca — possession-proof switch guarantees no other kind reaches here
        {
            (subjectType, displayName) = VgcaCertSubjectParser.Classify(cert.Subject);
            // Hard-reject only when cert is Personal (has CCCD) AND user also has a CCCD AND they mismatch.
            // Org cert (no CCCD → Organization) or user has no CCCD → accept without comparison.
            if (VgcaCertSubjectParser.TryExtractNationalId(cert.Subject, out var usbCertNationalId)
                && !string.IsNullOrWhiteSpace(user.SigningNationalId)
                && !string.Equals(
                    VgcaCertSubjectParser.NormalizeNationalId(usbCertNationalId),
                    VgcaCertSubjectParser.NormalizeNationalId(user.SigningNationalId),
                    StringComparison.Ordinal))
            {
                return Result.Fail(new BadRequestError(
                    "Certificate does not belong to this user.", "ENROLLMENT_CERT_NATIONAL_ID_MISMATCH"));
            }
        }

        // ── Idempotency + pending cap, then persist as Pending ──────────────────────────────────────
        var existing = await enrollments.GetByUserAsync(cmd.TenantId, cmd.CallerUserId, ct);

        // An active (non-revoked) row with the same serial+issuer → return it (idempotent). A Rejected row
        // is IsActive=false so it is NOT here → re-enroll after reject creates a fresh Pending row (H3).
        var duplicate = existing.FirstOrDefault(e =>
            e.IsActive
            && string.Equals(e.SerialNumber, cert.SerialNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Issuer, cert.Issuer, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            return Result.Ok(duplicate.PublicId);

        var pendingCount = existing.Count(e => e.ApprovalStatus == CertApprovalStatus.Pending);
        if (pendingCount >= MaxPendingEnrollments)
            return Result.Fail(new BadRequestError(
                "Too many pending enrollment requests; wait for admin review.", "ENROLLMENT_PENDING_LIMIT_REACHED"));

        var enrollment = SignerCertEnrollment.Create(
            tenantId:       cmd.TenantId,
            userId:         cmd.CallerUserId,
            serialNumber:   cert.SerialNumber,
            issuer:         cert.Issuer,
            thumbprint:     cert.Thumbprint,
            subject:        cert.Subject,
            enrolledBy:     cmd.CallerUserId,      // self-enrolled
            kind:           cmd.Kind,
            approvalStatus: CertApprovalStatus.Pending, // literal — self-service can never auto-approve (H2)
            subjectType:    subjectType,
            displayName:    displayName,
            gen:            guidGen);

        await enrollments.AddAsync(enrollment, ct);
        return Result.Ok(enrollment.PublicId);
    }

    /// <summary>USB: verify the client-supplied raw signature over the nonce against the supplied DER cert.</summary>
    private async Task<Result<CertificateInfo>> ProveUsbPossessionAsync(
        EnrollMySignatureCertCommand cmd, byte[] challenge, CancellationToken ct)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(cmd.SignedNonceBase64!);
        }
        catch (FormatException)
        {
            return Result.Fail(new BadRequestError("Signed challenge is not valid base64.", "ENROLLMENT_SIGNATURE_MALFORMED"));
        }

        var verify = await mediator.Send(new VerifySignatureCommand(
            TenantId:          cmd.TenantId,
            Payload:           challenge,          // raw nonce — the verifier hashes it internally
            Signature:         signature,
            OcspModeOverride:  null,
            SignerCertBase64:  cmd.CertBase64,
            HashAlg:           "SHA256",
            Encode:            "RSA",
            Format:            SignatureFormat.Raw), ct);

        if (verify.IsFailed || !verify.Value.IsValid || verify.Value.SigningCertificate is null)
            return Result.Fail(new BadRequestError(
                "USB possession proof failed — signature did not verify against the certificate.",
                "ENROLLMENT_POSSESSION_PROOF_FAILED"));

        return Result.Ok(verify.Value.SigningCertificate);
    }

    /// <summary>
    /// Remote: dispatch a server-side sign of the nonce through the tenant's configured provider via
    /// <see cref="SignPayloadCommand"/>. That path resolves the provider (must support server-side signing),
    /// maps the caller to their CA-side national id, and returns the signing certificate. For Mysign in sync
    /// mode the sign blocks on the signer's phone OTP — that OTP is the possession proof. Keeping the provider
    /// key out of this layer satisfies the provider-agnostic mandate (D6). An async/pending result carries no
    /// certificate → treated as a failed proof.
    /// <para>SkipBindingGuard: the anti-impersonation binding guard is deliberately bypassed here. Proving
    /// possession is what CREATES the binding, so requiring the cert to already be enrolled+approved is
    /// circular — any signer with a prior (e.g. USB) enrollment would be locked into the enrolled tier and
    /// could never enroll a second, not-yet-bound Mysign cert. Attribution is still enforced: the sign is
    /// issued against the caller's own SigningNationalId, and the enrollment is recorded Pending for approval.</para>
    /// </summary>
    private async Task<Result<CertificateInfo>> ProveRemotePossessionAsync(
        EnrollMySignatureCertCommand cmd, byte[] challenge, CancellationToken ct)
    {
        var sign = await mediator.Send(new SignPayloadCommand(
            TenantId:         cmd.TenantId,
            Payload:          challenge,
            UserId:           cmd.CallerUserId.ToString(), // internal Guid — handler resolves CCCD for attribution
            Reason:           "Signature certificate enrollment possession proof",
            ExtraParameters:  null,
            SkipBindingGuard: true), ct); // possession-proof establishes the binding → guard would be circular

        if (sign.IsFailed || !sign.Value.IsSuccess || sign.Value.SigningCertificate is null)
            return Result.Fail(new BadRequestError(
                "Remote possession proof failed — the signer did not complete the sign challenge.",
                "ENROLLMENT_POSSESSION_PROOF_FAILED"));

        return Result.Ok(sign.Value.SigningCertificate);
    }
}
