using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Contracts.Infrastructure.Persistence;
using AqtCoreFW.Contracts.Infrastructure.Persistence.Repositories;
using AqtCoreFW.Contracts.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Contracts.Infrastructure;

/// <summary>DI registration for Contracts module Infrastructure layer.</summary>
public static class ContractsInfrastructureRegistration
{
    public static IServiceCollection AddContractsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ContractsDbContext>((sp, opts) =>
        {
            opts.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "contracts"));

            opts.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IContractRepository, ContractRepository>();

        return services;
    }
}
