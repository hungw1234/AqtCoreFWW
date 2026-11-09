using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.ModuleName.Domain.Repositories;
using AqtCoreFW.ModuleName.Infrastructure.Persistence;
using AqtCoreFW.ModuleName.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.ModuleName.Infrastructure;

/// <summary>DI registration for ModuleName module Infrastructure layer.</summary>
public static class ModuleNameInfrastructureRegistration
{
    public static IServiceCollection AddModuleNameInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ModuleNameDbContext>((sp, opts) =>
        {
            opts.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "modulename"));

            opts.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IModuleNameRepository, ModuleNameRepository>();

        return services;
    }
}
