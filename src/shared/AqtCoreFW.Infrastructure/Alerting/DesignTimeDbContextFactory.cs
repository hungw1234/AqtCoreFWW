using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AqtCoreFW.Infrastructure.Alerting;

/// <summary>
/// Design-time factory for EF Core tooling (dotnet-ef migrations).
/// Only used by dotnet-ef CLI — not registered in the application DI container.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AlertingDbContext>
{
    public AlertingDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=AqtCoreFW;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true;MultipleActiveResultSets=true";

        var opts = new DbContextOptionsBuilder<AlertingDbContext>()
            .UseSqlServer(connStr, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "alerting"))
            .Options;

        return new AlertingDbContext(opts);
    }
}
