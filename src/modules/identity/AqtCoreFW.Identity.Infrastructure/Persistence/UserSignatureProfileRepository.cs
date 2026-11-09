using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUserSignatureProfileRepository"/>.
/// Soft-delete enforced by global query filter; tenant isolation via explicit TenantId predicates
/// (RLS is the DB-level backstop, explicit predicates are the application-level guard).
/// Mutation callers use load-then-mutate via the tracked EF Set&lt;T&gt;; SaveChangesAsync flushes.
/// </summary>
public sealed class UserSignatureProfileRepository(IdentityDbContext db) : IUserSignatureProfileRepository
{
    public async Task AddAsync(UserSignatureProfile profile, CancellationToken ct = default)
    {
        await db.UserSignatureProfiles.AddAsync(profile, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Scoped to tenant AND user — returning null for a PublicId that belongs to a different user
    /// or tenant prevents IDOR access (same-tenant user A cannot read user B's profile id).
    /// </summary>
    public Task<UserSignatureProfile?> GetByPublicIdAsync(
        Guid tenantId, Guid userId, Guid publicId, CancellationToken ct = default)
        => db.UserSignatureProfiles
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.UserId == userId && p.PublicId == publicId,
                ct);

    public async Task<IReadOnlyList<UserSignatureProfile>> ListByUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
        => await db.UserSignatureProfiles
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.UserId == userId)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);

    public Task<UserSignatureProfile?> GetDefaultAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
        => db.UserSignatureProfiles
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.UserId == userId && p.IsDefault,
                ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
