using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>EF Core repository for IpFilterRule — T1, write-side only.</summary>
public sealed class IpFilterRuleRepository(IdentityDbContext db) : IIpFilterRuleRepository
{
    public async Task<IpFilterRule?> GetByIdAsync(long id, CancellationToken ct = default)
        => await db.IpFilterRules.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<IpFilterRule>> ListAllEnabledAsync(CancellationToken ct = default)
        => await db.IpFilterRules
            .Where(r => r.IsEnabled)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<IpFilterRule>> ListAllAsync(CancellationToken ct = default)
        => await db.IpFilterRules
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(IpFilterRule entity, CancellationToken ct = default)
    {
        db.IpFilterRules.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(IpFilterRule entity, CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);

    public async Task DeleteAsync(IpFilterRule entity, CancellationToken ct = default)
    {
        entity.Delete();
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCidrAsync(Guid? tenantId, string cidr, long? excludeId = null, CancellationToken ct = default)
        => await db.IpFilterRules
            .Where(r => r.TenantId == tenantId
                     && r.Cidr == cidr
                     && (excludeId == null || r.Id != excludeId))
            .AnyAsync(ct);
}
