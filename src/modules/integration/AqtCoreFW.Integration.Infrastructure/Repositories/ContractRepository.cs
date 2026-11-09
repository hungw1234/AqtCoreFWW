using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Repositories;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Integration.Infrastructure.Repositories;

public sealed class ContractRepository(IntegrationDbContext dbContext) : IContractRepository
{
    public async Task<Contract?> GetByPublicIdAsync(Guid publicId, Guid tenantId, CancellationToken ct)
        => await dbContext.Contracts.FirstOrDefaultAsync(c => c.PublicId == publicId && c.TenantId == tenantId, ct);

    public async Task<Contract?> GetByInternalIdAsync(long id, CancellationToken ct)
        => await dbContext.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(Contract contract, CancellationToken ct)
    {
        dbContext.Contracts.Add(contract);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Contract contract, CancellationToken ct)
    {
        if (dbContext.Entry(contract).State == EntityState.Detached)
            dbContext.Contracts.Update(contract);
        await dbContext.SaveChangesAsync(ct);
    }
}
