using AqtCoreFW.AssetManagement.Domain.Entities;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.AssetManagement.Infrastructure.Persistence;

public sealed class AssetManagementDbContext(
    DbContextOptions<AssetManagementDbContext> options,
    ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "assetmanagement";

    public DbSet<AssetManagementEntity> AssetManagements => Set<AssetManagementEntity>();
}
