using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Organization.Infrastructure.Persistence;

/// <summary>EF Core DbContext for organization module — schema "organization".</summary>
public class OrgDbContext(DbContextOptions<OrgDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "organization";

    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<UserOrgUnitAssignment> Assignments => Set<UserOrgUnitAssignment>();
    public DbSet<StaffPositionHistory> PositionHistories => Set<StaffPositionHistory>();

    // Phase-04d: entity configurations extracted to Persistence/Configurations/*.cs
    // and auto-discovered by base ModuleDbContext.OnModelCreating via ApplyConfigurationsFromAssembly.
}
