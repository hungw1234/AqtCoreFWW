using AqtCoreFW.ModuleName.Domain.Entities;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.ModuleName.Infrastructure.Persistence;

public sealed class ModuleNameDbContext(
    DbContextOptions<ModuleNameDbContext> options,
    ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "modulename";

    public DbSet<ModuleNameEntity> ModuleNames => Set<ModuleNameEntity>();
}
