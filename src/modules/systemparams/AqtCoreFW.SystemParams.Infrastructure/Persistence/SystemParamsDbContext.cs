using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.SystemParams.Infrastructure.Persistence;

/// <summary>EF Core DbContext for system params — schema "config".</summary>
public class SystemParamsDbContext(DbContextOptions<SystemParamsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "config";

    public DbSet<SystemParameter> SystemParameters => Set<SystemParameter>();
    public DbSet<SystemAnnouncement> SystemAnnouncements => Set<SystemAnnouncement>();

    // Phase-04d: entity configurations extracted to Persistence/Configurations/*.cs
    // and auto-discovered by base ModuleDbContext.OnModelCreating via ApplyConfigurationsFromAssembly.
}
