using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Contracts.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Contracts.Infrastructure.Persistence;

public sealed class ContractsDbContext(
    DbContextOptions<ContractsDbContext> options,
    ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "contracts";

    public DbSet<Contract> Contracts => Set<Contract>();
}
