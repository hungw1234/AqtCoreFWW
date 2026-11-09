using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Infrastructure.Backup;

/// <summary>DI registration for backup infrastructure — BackupDbContext + DatabaseBackupService.</summary>
public static class BackupRegistration
{
    public static IServiceCollection AddBackupInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required for BackupDbContext.");

        services.AddDbContext<BackupDbContext>(opts =>
            opts.UseSqlServer(connStr, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "backup")));

        services.AddScoped<DatabaseBackupService>();

        return services;
    }
}
