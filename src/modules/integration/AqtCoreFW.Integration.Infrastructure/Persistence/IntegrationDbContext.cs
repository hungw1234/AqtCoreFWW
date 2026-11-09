using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Integration.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Integration.Infrastructure.Persistence;

/// <summary>
/// Integration module DB context — owns the "integration" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// </summary>
public sealed class IntegrationDbContext(
    DbContextOptions<IntegrationDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "integration";

    public DbSet<Partner> Partners { get; set; } = default!;
    public DbSet<Contract> Contracts { get; set; } = default!;
    public DbSet<MessageLog> MessageLogs { get; set; } = default!;
}
