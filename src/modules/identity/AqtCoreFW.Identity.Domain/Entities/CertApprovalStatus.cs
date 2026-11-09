namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Admin-controlled approval lifecycle for a <see cref="SignerCertEnrollment"/>.
/// Self-service enrollments start at <see cref="Pending"/>; only admin-<see cref="Approved"/>
/// certs may be used to satisfy the cert==signer binding guard (enrolled tier).
/// </summary>
public enum CertApprovalStatus
{
    /// <summary>Awaiting admin review. Cannot bind/sign in enrolled-tier mode.</summary>
    Pending = 1,

    /// <summary>Admin approved. Eligible for the enrolled-tier binding check.</summary>
    Approved = 2,

    /// <summary>Admin rejected. Row kept for audit; IsActive set to false so re-enroll is permitted.</summary>
    Rejected = 3,
}
