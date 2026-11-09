using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>EF Core repository for ExternalIdentity — write-side only.</summary>
public sealed class ExternalIdentityRepository(IdentityDbContext db) : IExternalIdentityRepository
{
    public async Task<ExternalIdentity?> GetByIdAsync(long id, CancellationToken ct = default)
        => await db.ExternalIdentities.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<ExternalIdentity?> GetByUserAndProviderAsync(
        Guid userId, ExternalIdentityProvider provider, CancellationToken ct = default)
        => await db.ExternalIdentities
            .FirstOrDefaultAsync(e => e.UserId == userId && e.Provider == provider, ct);

    public async Task<ExternalIdentity?> GetByProviderAndExternalIdAsync(
        ExternalIdentityProvider provider, string externalId, CancellationToken ct = default)
        => await db.ExternalIdentities
            .FirstOrDefaultAsync(e => e.Provider == provider && e.ExternalId == externalId, ct);

    public async Task<IReadOnlyList<ExternalIdentity>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await db.ExternalIdentities
            .Where(e => e.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task AddAsync(ExternalIdentity entity, CancellationToken ct = default)
    {
        db.ExternalIdentities.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ExternalIdentity entity, CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);

    public async Task DeleteAsync(ExternalIdentity entity, CancellationToken ct = default)
    {
        entity.Delete();
        await db.SaveChangesAsync(ct);
    }
}
