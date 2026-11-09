using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core repository for TenantSignatureConfig — T1 singleton per tenant, write-side only.
/// Read-side query handlers use db.Set&lt;TenantSignatureConfig&gt;() directly (no DbSet property
/// in IdentityDbContext — entity is registered via ApplyConfigurationsFromAssembly in W2).
/// </summary>
public sealed class TenantSignatureConfigRepository(IdentityDbContext db)
    : ITenantSignatureConfigRepository
{
    public async Task<TenantSignatureConfig?> GetByTenantIdAsync(
        Guid tenantId, CancellationToken ct = default)
        => await db.Set<TenantSignatureConfig>()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);

    public async Task AddAsync(TenantSignatureConfig entity, CancellationToken ct = default)
    {
        db.Set<TenantSignatureConfig>().Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TenantSignatureConfig entity, byte[]? expectedRowVersion = null, CancellationToken ct = default)
    {
        // Optimistic concurrency: if the caller supplies the RowVersion it read at GET time,
        // override EF's tracked original value so EF appends WHERE RowVersion = @expected.
        // On stale write EF throws DbUpdateConcurrencyException → GlobalExceptionHandler → 409.
        if (expectedRowVersion is not null)
            db.Entry(entity).Property(e => e.RowVersion).OriginalValue = expectedRowVersion;

        await db.SaveChangesAsync(ct);
    }
}
