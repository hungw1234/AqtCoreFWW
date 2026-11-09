using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Integration.Application;
using AqtCoreFW.Integration.Domain.Repositories;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using AqtCoreFW.Integration.Infrastructure.Repositories;
using AqtCoreFW.Integration.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Integration.Infrastructure;

public static class IntegrationInfrastructureRegistration
{
    public static IServiceCollection AddIntegrationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<IntegrationDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "integration"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        services.AddScoped<IPartnerRepository, PartnerRepository>();
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IMessageLogRepository, MessageLogRepository>();

        // Tenant-agnostic demo integration seeder (IHostedService, conditional on config). Seeds one
        // Active Partner + one Draft Contract per TenantSeedProfiles entry. Default OFF (Development
        // appsettings + the reset-all script enable it). Self-contained — no cross-module deps, no
        // tenant-existence guard (a partner's TenantId is an unenforced stamp).
        services.Configure<DemoIntegrationSeederOptions>(
            configuration.GetSection(DemoIntegrationSeederOptions.SectionName));
        var demoIntegrationOpts = configuration
            .GetSection(DemoIntegrationSeederOptions.SectionName)
            .Get<DemoIntegrationSeederOptions>();
        if (demoIntegrationOpts?.Enabled == true)
        {
            services.AddHostedService<DemoIntegrationSeeder>();
        }

        return services;
    }
}
