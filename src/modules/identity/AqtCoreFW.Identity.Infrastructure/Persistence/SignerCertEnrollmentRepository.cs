using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ISignerCertEnrollmentRepository"/>.
/// Soft-delete is enforced by the entity's global query filter; tenant isolation by RLS + the
/// explicit TenantId predicates here.
/// </summary>
public sealed class SignerCertEnrollmentRepository(IdentityDbContext db) : ISignerCertEnrollmentRepository
{
    public async Task<IReadOnlyList<SignerCertEnrollment>> GetActiveByUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
        => await db.SignerCertEnrollments
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.UserId == userId && e.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SignerCertEnrollment>> GetByUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
        => await db.SignerCertEnrollments
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.UserId == userId)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync(ct);

    public Task<SignerCertEnrollment?> GetByIdAsync(Guid tenantId, long id, CancellationToken ct = default)
        => db.SignerCertEnrollments
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, ct);

    /// <summary>
    /// Resolves enrollment by PublicId scoped to tenant (M1 — anti-IDOR).
    /// AsNoTracking: admin read path; mutations go via tracked GetByIdAsync.
    /// </summary>
    public Task<SignerCertEnrollment?> GetByPublicIdAsync(
        Guid tenantId, Guid publicId, CancellationToken ct = default)
        => db.SignerCertEnrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.PublicId == publicId, ct);

    /// <summary>
    /// Returns true if the user has ANY non-soft-deleted enrollment row regardless of IsActive or
    /// ApprovalStatus (H1 — all-status query). Does NOT reuse GetActiveByUserAsync which filters
    /// IsActive=true and would miss Rejected/revoked rows, silently re-enabling the CCCD bootstrap.
    /// </summary>
    public Task<bool> HasAnyEnrollmentAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
        => db.SignerCertEnrollments
            // Global query filter already excludes IsDeleted=true rows — soft-deleted do not count.
            // No IsActive / ApprovalStatus filter: all live rows (Pending, Approved, Rejected) count.
            .AnyAsync(e => e.TenantId == tenantId && e.UserId == userId, ct);

    public async Task<(IReadOnlyList<SignerCertEnrollment> Items, int Total)> ListPendingAsync(
        Guid tenantId, int skip, int take, CancellationToken ct = default)
    {
        var query = db.SignerCertEnrollments
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ApprovalStatus == CertApprovalStatus.Pending);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(e => e.EnrolledAt) // FIFO review queue — oldest pending first
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task AddAsync(SignerCertEnrollment enrollment, CancellationToken ct = default)
    {
        await db.SignerCertEnrollments.AddAsync(enrollment, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
