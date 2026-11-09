using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence;

/// <summary>
/// Reporting module DB context — owns the "reporting" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// </summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "reporting";

    public DbSet<ReportDefinition> ReportDefinitions { get; set; } = default!;
    public DbSet<ReportExecution> ReportExecutions { get; set; } = default!;
    public DbSet<QueryCatalogEntry> QueryCatalogEntries { get; set; } = default!;
    public DbSet<ReportWidget> ReportWidgets { get; set; } = default!;
    public DbSet<DashboardLayout> DashboardLayouts { get; set; } = default!;
}
