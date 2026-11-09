using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AqtCoreFW.ModuleName.Infrastructure.Persistence;

internal sealed class DesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<ModuleNameDbContext>
{
    public ModuleNameDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost;Database=AqtCoreFW;Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ModuleNameDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "modulename"))
            .Options;

        return new ModuleNameDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsSystemAdmin => true;
    }
}
