using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>EF Core repository for CredentialPolicy — write-side only.</summary>
public sealed class CredentialPolicyRepository(IdentityDbContext db) : ICredentialPolicyRepository
{
    public async Task<CredentialPolicy?> GetByIdAsync(long id, CancellationToken ct = default)
        => await db.CredentialPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<CredentialPolicy?> GetDefaultForTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await db.CredentialPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.IsDefault, ct);

    public async Task<IReadOnlyList<CredentialPolicy>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await db.CredentialPolicies
            .Where(p => p.TenantId == tenantId)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task AddAsync(CredentialPolicy entity, CancellationToken ct = default)
    {
        db.CredentialPolicies.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CredentialPolicy entity, CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);

    public async Task DeleteAsync(CredentialPolicy entity, CancellationToken ct = default)
    {
        entity.Delete();
        await db.SaveChangesAsync(ct);
    }
}
