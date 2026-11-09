using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AqtCoreFW.AssetManagement.Infrastructure.Persistence;

internal sealed class DesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AssetManagementDbContext>
{
    public AssetManagementDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost;Database=AqtCoreFW;Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<AssetManagementDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "assetmanagement"))
            .Options;

        return new AssetManagementDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
        public bool IsSystemAdmin => true;
    }
}
