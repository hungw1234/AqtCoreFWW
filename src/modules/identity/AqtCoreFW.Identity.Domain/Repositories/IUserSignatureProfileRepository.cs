using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>
/// Read/write access to <see cref="UserSignatureProfile"/> aggregates — user-owned signing profiles
/// that carry visual signature images and an optional cert enrollment link.
/// </summary>
public interface IUserSignatureProfileRepository
{
    /// <summary>Persists a new profile (insert + SaveChanges).</summary>
    Task AddAsync(UserSignatureProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Resolves a profile by its public (anti-IDOR) id, scoped to tenant AND user.
    /// Returns null if the PublicId belongs to a different tenant or a different user.
    /// </summary>
    Task<UserSignatureProfile?> GetByPublicIdAsync(
        Guid tenantId, Guid userId, Guid publicId, CancellationToken ct = default);

    /// <summary>All non-deleted profiles for a user within a tenant, ordered by DisplayName.</summary>
    Task<IReadOnlyList<UserSignatureProfile>> ListByUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The current default profile for a user, or null if no default is set.
    /// At most one profile per (TenantId, UserId) may be default (filtered unique index backs this).
    /// </summary>
    Task<UserSignatureProfile?> GetDefaultAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Persists pending mutations on tracked profiles (load-then-mutate pattern via EF tracked Set&lt;T&gt;).
    /// Does NOT call AddAsync — mutation callers load the entity, call domain methods, then call this.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
