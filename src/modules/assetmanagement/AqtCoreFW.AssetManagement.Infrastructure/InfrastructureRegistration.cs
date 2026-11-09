using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.AssetManagement.Domain.Repositories;
using AqtCoreFW.AssetManagement.Infrastructure.Persistence;
using AqtCoreFW.AssetManagement.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.AssetManagement.Infrastructure;

/// <summary>DI registration for AssetManagement module Infrastructure layer.</summary>
public static class AssetManagementInfrastructureRegistration
{
    public static IServiceCollection AddAssetManagementInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AssetManagementDbContext>((sp, opts) =>
        {
            opts.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "assetmanagement"));

            opts.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IAssetManagementRepository, AssetManagementRepository>();

        return services;
    }
}
