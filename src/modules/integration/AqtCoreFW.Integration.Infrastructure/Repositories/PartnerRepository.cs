using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Repositories;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Integration.Infrastructure.Repositories;

public sealed class PartnerRepository(IntegrationDbContext dbContext) : IPartnerRepository
{
    public async Task<Partner?> GetByPublicIdAsync(Guid publicId, Guid tenantId, CancellationToken ct = default)
        => await dbContext.Partners.FirstOrDefaultAsync(p => p.PublicId == publicId && p.TenantId == tenantId, ct);

    public async Task AddAsync(Partner partner, CancellationToken ct)
    {
        dbContext.Partners.Add(partner);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Partner partner, CancellationToken ct)
    {
        if (dbContext.Entry(partner).State == EntityState.Detached)
            dbContext.Partners.Update(partner);
        await dbContext.SaveChangesAsync(ct);
    }
}
