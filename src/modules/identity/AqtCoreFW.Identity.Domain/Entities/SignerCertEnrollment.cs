using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Per-signer enrolled signing certificate — the forge-resistant binding key for the
/// "the signing cert provably belongs to the assigned signer" invariant (anti-impersonation).
///
/// <para>When a signer has an active enrollment, a signature is only accepted if the signing
/// certificate's serial+issuer (or thumbprint) byte-matches the enrolled values. This is stronger
/// than the CCCD-from-subject bootstrap fallback (a CCCD is not cryptographically bound to a key):
/// an attacker would have to present a cert with the exact enrolled serial AND issuer.</para>
///
/// <para>Bootstrap: before a signer is enrolled, the binding guard falls back to comparing the CCCD
/// extracted from the cert subject against <see cref="ApplicationUser.SigningNationalId"/>. Enrollment
/// is the tighten-over-time path — enrol once the signer's real cert identity is known.</para>
///
/// <para>Approval lifecycle (P1 expansion): self-service enrollments start at
/// <see cref="CertApprovalStatus.Pending"/>; admin-driven enrollments (e.g. EnrollSignerCertCommand)
/// pass <see cref="CertApprovalStatus.Approved"/> explicitly. Only Approved enrollments satisfy the
/// enrolled-tier binding check. <see cref="Reject"/> sets <see cref="IsActive"/>=false so the unique
/// serial index (filtered WHERE IsActive=1) does not block re-enroll with the same serial (H3).</para>
///
/// <para>Schema: [identity]. Tenant-scoped (a signer belongs to one tenant); RLS-isolated.
/// PK: long Id (AuditableEntity base). T2: Guid <see cref="PublicId"/> AK (M1 anti-IDOR).
/// Unique per (TenantId, UserId, SerialNumber, Issuer) WHERE IsActive=1 AND IsDeleted=0.</para>
/// </summary>
public class SignerCertEnrollment : AuditableEntity, ITenantScoped, IPublicIdentified
{
    /// <inheritdoc/>
    public Guid PublicId { get; private set; }

    /// <inheritdoc/>
    public Guid TenantId { get; private set; }

    /// <summary>The signer this certificate is enrolled for (FK to AspNetUsers.Id).</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Certificate serial number (hex string, exactly as <see cref="System.Security.Cryptography.X509Certificates.X509Certificate2.SerialNumber"/>
    /// reports it). Compared together with <see cref="Issuer"/> — serial is only unique per issuer.
    /// </summary>
    public string SerialNumber { get; private set; } = string.Empty;

    /// <summary>Issuer distinguished name (the CA that issued the cert).</summary>
    public string Issuer { get; private set; } = string.Empty;

    /// <summary>
    /// SHA-256 thumbprint of the DER-encoded certificate — an alternative exact-match key that pins
    /// the whole certificate, not just serial+issuer. Populated at enrollment; usable on its own.
    /// </summary>
    public string Thumbprint { get; private set; } = string.Empty;

    /// <summary>Subject DN captured at enrollment time (audit / human reference; not used for matching).</summary>
    public string? Subject { get; private set; }

    /// <summary>When false the enrollment is revoked/rejected and no longer satisfies the binding.</summary>
    public bool IsActive { get; private set; }

    /// <summary>UTC timestamp the certificate was enrolled.</summary>
    public DateTime EnrolledAt { get; private set; }

    // ── Expansion: provider kind + approval lifecycle (P1) ────────────────────────────────────

    /// <summary>
    /// Which signing infrastructure this certificate belongs to (Remote Mysign / USB VGCA).
    /// Provenance only — binding logic stays provider-agnostic (serial+issuer/thumbprint).
    /// </summary>
    public EnrollmentKind Kind { get; private set; }

    /// <summary>
    /// Admin-controlled approval state. Only <see cref="CertApprovalStatus.Approved"/> enrollments
    /// satisfy the enrolled-tier binding check. Self-service enrollments start <see cref="CertApprovalStatus.Pending"/>.
    /// </summary>
    public CertApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>Id of the admin who approved or rejected this enrollment (null until acted on).</summary>
    public Guid? ApprovedBy { get; private set; }

    /// <summary>UTC timestamp of the approve/reject decision (null until acted on).</summary>
    public DateTime? ApprovedAt { get; private set; }

    /// <summary>Admin-supplied reason when rejecting (required on Reject; ≤512 chars).</summary>
    public string? RejectionReason { get; private set; }

    // ── Expansion: subject-type classification + display name (Phase 01 decouple) ─────────────

    /// <summary>
    /// Whether the enrolled certificate belongs to a natural person (has CCCD/CMND) or an organisation.
    /// Derived from the cert subject DN at enrollment time via
    /// <see cref="AqtCoreFW.Identity.Domain.Signing.VgcaCertSubjectParser.Classify"/>.
    /// Display/audit only — never used as a binding or access-control gate.
    /// Historical rows default to <see cref="CertSubjectType.Personal"/> (0).
    /// </summary>
    public CertSubjectType SubjectType { get; private set; }

    /// <summary>
    /// Human-readable display name derived from the cert subject at enrollment time:
    /// Personal ⇒ CN value; Organisation ⇒ O value (falling back to CN).
    /// Display/audit only — never a gate. May be null for historical rows.
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>EF Core: private parameterless constructor for materialisation.</summary>
    private SignerCertEnrollment() { }

    /// <summary>
    /// Factory — enrols a signing certificate for a signer. Serial + issuer (or thumbprint) become the
    /// binding key. All identity fields are required: an enrollment with no key would silently weaken
    /// the invariant back to the CCCD-subject fallback.
    ///
    /// <para><b>H2 contract:</b> <paramref name="kind"/> and <paramref name="approvalStatus"/> are
    /// REQUIRED with no defaults — caller must be explicit. Admin path passes
    /// <see cref="CertApprovalStatus.Approved"/>; self-service handler passes
    /// <see cref="CertApprovalStatus.Pending"/> as a hardcoded literal (never caller-supplied).</para>
    /// </summary>
    public static SignerCertEnrollment Create(
        Guid tenantId,
        Guid userId,
        string serialNumber,
        string issuer,
        string thumbprint,
        string? subject,
        Guid enrolledBy,
        EnrollmentKind kind,
        CertApprovalStatus approvalStatus,
        CertSubjectType subjectType,
        string? displayName,
        IGuidGenerator gen)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new ArgumentException("SerialNumber must not be empty.", nameof(serialNumber));
        if (string.IsNullOrWhiteSpace(issuer))
            throw new ArgumentException("Issuer must not be empty.", nameof(issuer));
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new ArgumentException("Thumbprint must not be empty.", nameof(thumbprint));
        if (enrolledBy == Guid.Empty)
            throw new ArgumentException("EnrolledBy must not be empty.", nameof(enrolledBy));

        var enrollment = new SignerCertEnrollment
        {
            // PublicId: v4 random — enrollment id should not leak enrollment timing via timestamp
            PublicId       = gen.NewIdRandom(),
            TenantId       = tenantId,
            UserId         = userId,
            SerialNumber   = serialNumber.Trim(),
            Issuer         = issuer.Trim(),
            Thumbprint     = thumbprint.Trim(),
            Subject        = subject,
            IsActive       = true,
            EnrolledAt     = DateTime.UtcNow,
            Kind           = kind,
            ApprovalStatus = approvalStatus,
            SubjectType    = subjectType,
            // Display/audit only — cap at the column length (nvarchar(256)) so a pathological O/CN value
            // can never throw a truncation SqlException on persist.
            DisplayName    = displayName is { Length: > 256 } d ? d[..256] : displayName,
        };
        enrollment.SetAuditCreate(enrolledBy);
        return enrollment;
    }

    /// <summary>
    /// Approves this enrollment. Transitions from <see cref="CertApprovalStatus.Pending"/> to
    /// <see cref="CertApprovalStatus.Approved"/>. Throws if the enrollment is already terminal
    /// (Approved or Rejected) — re-enrollment creates a fresh Pending row.
    /// </summary>
    public void Approve(Guid adminId)
    {
        if (adminId == Guid.Empty)
            throw new ArgumentException("AdminId must not be empty.", nameof(adminId));

        if (ApprovalStatus == CertApprovalStatus.Approved)
            throw new InvalidOperationException(
                "Enrollment is already Approved. Create a new enrollment to replace it.");

        if (ApprovalStatus == CertApprovalStatus.Rejected)
            throw new InvalidOperationException(
                "Enrollment is Rejected. Create a new enrollment to re-enrol this certificate.");

        ApprovalStatus = CertApprovalStatus.Approved;
        ApprovedBy     = adminId;
        ApprovedAt     = DateTime.UtcNow;
        SetAuditUpdate(adminId);
    }

    /// <summary>
    /// Rejects this enrollment. Transitions from <see cref="CertApprovalStatus.Pending"/> to
    /// <see cref="CertApprovalStatus.Rejected"/> and sets <see cref="IsActive"/>=false (H3) so the
    /// filtered unique serial index (WHERE IsActive=1) does not block a new Pending row for the same
    /// serial+issuer (re-enroll after rejection must be possible).
    /// Throws if already terminal (Approved or Rejected).
    /// </summary>
    public void Reject(Guid adminId, string reason)
    {
        if (adminId == Guid.Empty)
            throw new ArgumentException("AdminId must not be empty.", nameof(adminId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("RejectionReason must not be empty.", nameof(reason));
        if (reason.Length > 512)
            throw new ArgumentException("RejectionReason must not exceed 512 characters.", nameof(reason));

        if (ApprovalStatus == CertApprovalStatus.Approved)
            throw new InvalidOperationException(
                "Enrollment is already Approved. Revoke it instead of rejecting.");

        if (ApprovalStatus == CertApprovalStatus.Rejected)
            throw new InvalidOperationException(
                "Enrollment is already Rejected.");

        ApprovalStatus  = CertApprovalStatus.Rejected;
        RejectionReason = reason.Trim();
        ApprovedBy      = adminId;
        ApprovedAt      = DateTime.UtcNow;
        // H3: IsActive=false removes this row from the filtered unique index so re-enroll is unblocked.
        IsActive = false;
        SetAuditUpdate(adminId);
    }

    /// <summary>Revokes this enrollment (soft state — keeps the row for audit).</summary>
    public void Revoke(Guid revokedBy)
    {
        if (!IsActive) return;
        IsActive = false;
        SetAuditUpdate(revokedBy);
    }
}
