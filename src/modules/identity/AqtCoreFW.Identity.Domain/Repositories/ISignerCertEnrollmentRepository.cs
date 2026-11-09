using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>
/// Read/write access to <see cref="SignerCertEnrollment"/> — the per-signer enrolled signing
/// certificate store used by the cert==assigned-signer binding guard.
/// </summary>
public interface ISignerCertEnrollmentRepository
{
    /// <summary>Active (non-revoked, non-deleted) enrollments for a signer within a tenant.</summary>
    Task<IReadOnlyList<SignerCertEnrollment>> GetActiveByUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>All enrollments (active + revoked) for a signer — admin listing.</summary>
    Task<IReadOnlyList<SignerCertEnrollment>> GetByUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Loads a single enrollment by id within the tenant, or null.</summary>
    Task<SignerCertEnrollment?> GetByIdAsync(Guid tenantId, long id, CancellationToken ct = default);

    /// <summary>
    /// Resolves a single enrollment by its public (anti-IDOR) id within the tenant, or null.
    /// Scoped to tenant — a PublicId from a different tenant returns null (M1).
    /// </summary>
    Task<SignerCertEnrollment?> GetByPublicIdAsync(Guid tenantId, Guid publicId, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the user has ANY non-soft-deleted enrollment row regardless of
    /// <see cref="SignerCertEnrollment.IsActive"/> or <see cref="SignerCertEnrollment.ApprovalStatus"/>.
    /// Used by the binding guard (H1): if any enrollment row exists the user is in the enrolled tier
    /// (bootstrap CCCD fallback is suppressed even for revoked/Rejected rows — prevents silent downgrade).
    /// Do NOT use <see cref="GetActiveByUserAsync"/> for this check: that method filters IsActive=true
    /// and would miss Rejected/revoked rows, silently re-enabling the weaker CCCD bootstrap.
    /// </summary>
    Task<bool> HasAnyEnrollmentAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Lists Pending enrollments awaiting admin review within a tenant, oldest-first (FIFO queue),
    /// with a page slice and the total Pending count. Tenant-scoped — an admin only sees their own
    /// tenant's queue. Uses the (TenantId, UserId, ApprovalStatus) index.
    /// </summary>
    Task<(IReadOnlyList<SignerCertEnrollment> Items, int Total)> ListPendingAsync(
        Guid tenantId, int skip, int take, CancellationToken ct = default);

    /// <summary>Persists a new enrollment.</summary>
    Task AddAsync(SignerCertEnrollment enrollment, CancellationToken ct = default);

    /// <summary>Persists pending mutations (e.g. after Revoke, Approve, Reject).</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
