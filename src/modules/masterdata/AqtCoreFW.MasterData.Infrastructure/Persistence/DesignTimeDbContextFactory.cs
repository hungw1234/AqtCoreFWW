using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AqtCoreFW.MasterData.Infrastructure.Persistence;

/// <summary>EF design-time factory — used by dotnet ef migrations add/update.</summary>
public class DesignTimeMasterDataDbContextFactory : IDesignTimeDbContextFactory<MasterDataDbContext>
{
    public MasterDataDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=AqtCoreFW;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true;MultipleActiveResultSets=true";

var opts = new DbContextOptionsBuilder<MasterDataDbContext>()
    .UseSqlServer(connStr, sql =>
        sql.MigrationsHistoryTable("__EFMigrationsHistory", "masterdata"))
    .Options;
        return new MasterDataDbContext(opts, new NoOpTenantContext());
    }

    // Minimal no-op for design-time — not used at runtime
    private sealed class NoOpTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsSystemAdmin => true;
    }
}
