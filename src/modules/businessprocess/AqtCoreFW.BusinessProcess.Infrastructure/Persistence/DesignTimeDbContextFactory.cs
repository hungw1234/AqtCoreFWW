using AqtCoreFW.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AqtCoreFW.BusinessProcess.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core tooling (migrations, script generation).
/// Reads connection string from environment or falls back to local dev defaults.
/// Only used by dotnet-ef CLI — not registered in the application DI container.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BusinessProcessDbContext>
{
    public BusinessProcessDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=AqtCoreFW;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true;MultipleActiveResultSets=true";

        var opts = new DbContextOptionsBuilder<BusinessProcessDbContext>()
            .UseSqlServer(connStr, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "businessprocess"))
            .Options;

        return new BusinessProcessDbContext(opts, NullTenantContext.Instance);
    }
}
