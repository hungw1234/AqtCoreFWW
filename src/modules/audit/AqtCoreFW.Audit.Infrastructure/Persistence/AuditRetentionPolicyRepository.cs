using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Compliance;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Audit.Infrastructure.Persistence;

/// <summary>
/// EF repository for <see cref="AuditRetentionPolicy"/>. Mutating ops bypass the
/// AuditDbContext append-only guard because <see cref="AuditRetentionPolicy"/> is
/// intentionally NOT in the immutable-entity set (see <c>AuditDbContext.IsImmutableAuditEntity</c>).
/// </summary>
public sealed class AuditRetentionPolicyRepository(AuditDbContext db) : IAuditRetentionPolicyRepository
{
    public Task<AuditRetentionPolicy?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        db.AuditRetentionPolicies
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AuditRetentionPolicy>> ListAsync(
        Guid? tenantIdFilter,
        CancellationToken cancellationToken = default)
    {
        var q = db.AuditRetentionPolicies.AsNoTracking();
        if (tenantIdFilter.HasValue)
            q = q.Where(p => p.TenantId == tenantIdFilter || p.TenantId == null);
        return await q
            .OrderBy(p => p.TenantId == null ? 0 : 1)
            .ThenBy(p => p.Category)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(AuditRetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        db.AuditRetentionPolicies.Add(policy);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AuditRetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        db.AuditRetentionPolicies.Update(policy);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(AuditRetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        db.AuditRetentionPolicies.Remove(policy);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuditRetentionPolicy?> ResolveAsync(
        Guid tenantId,
        AuditCategory category,
        CancellationToken cancellationToken = default)
    {
        // Precedence: (tenant + cat) → (tenant + null cat) → (null tenant + null cat).
        // Single query: pull every potentially-matching row, rank in memory. Cheap because
        // the table is admin-curated (handful of rows).
        var candidates = await db.AuditRetentionPolicies.AsNoTracking()
            .Where(p => (p.TenantId == tenantId || p.TenantId == null)
                     && (p.Category == category || p.Category == null))
            .ToListAsync(cancellationToken);

        return candidates
            .OrderByDescending(p => p.TenantId == tenantId ? 2 : 0)
            .ThenByDescending(p => p.Category == category ? 1 : 0)
            .FirstOrDefault();
    }
}
