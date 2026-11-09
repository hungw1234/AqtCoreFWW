using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.BusinessProcess.Infrastructure.Persistence;

/// <summary>
/// BusinessProcess module DB context — owns the "businessprocess" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// </summary>
public sealed class BusinessProcessDbContext(
    DbContextOptions<BusinessProcessDbContext> options,
    ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "businessprocess";

    public DbSet<BusinessProcessDefinition> BusinessProcessDefinitions { get; set; } = default!;
    public DbSet<BusinessProcessStateForm> BusinessProcessStateForms { get; set; } = default!;
    public DbSet<BusinessProcessTransitionForm> BusinessProcessTransitionForms { get; set; } = default!;
}
