using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AqtCoreFW.SystemParams.Infrastructure.Persistence;

/// <summary>EF design-time factory — used by dotnet ef migrations add/update.</summary>
public class DesignTimeSystemParamsDbContextFactory : IDesignTimeDbContextFactory<SystemParamsDbContext>
{
    public SystemParamsDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=AqtCoreFW;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true;MultipleActiveResultSets=true";

        var opts = new DbContextOptionsBuilder<SystemParamsDbContext>()
            .UseSqlServer(connStr)
            .Options;
        return new SystemParamsDbContext(opts, new NoOpTenantContext());
    }

    private sealed class NoOpTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsSystemAdmin => true;
    }
}
