using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Rules.Domain.Repositories;
using AqtCoreFW.Rules.Domain.Services;
using AqtCoreFW.Rules.Infrastructure.Clients;
using AqtCoreFW.Rules.Infrastructure.Engine;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.Rules.Infrastructure.Repositories;
using AqtCoreFW.Rules.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Rules.Infrastructure;

public static class RulesInfrastructureRegistration
{
    public static IServiceCollection AddRulesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // DbContext — "rules" schema, separate migrations history table
        services.AddDbContext<RulesDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "rules"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // IMemoryCache — used by RulesEngineEvaluator for workflow caching
        services.AddMemoryCache();

        // Domain evaluator
        services.AddScoped<IRuleEvaluator, RulesEngineEvaluator>();

        // Cross-pod cache invalidation subscriber (IConnectionMultiplexer registered globally in AddInfrastructure)
        services.AddHostedService<RulesEngineCacheSubscriber>();

        // Repositories
        services.AddScoped<IRuleSetRepository, RuleSetRepository>();
        services.AddScoped<IDecisionTableRepository, DecisionTableRepository>();
        services.AddScoped<IRuleTestCaseRepository, RuleTestCaseRepository>();

        // Cross-module client — in-process for monolith
        services.AddScoped<IRuleEngineModuleClient, InProcessRuleEngineModuleClient>();

        // Tenant-agnostic demo rules seeder (IHostedService, conditional on config). Seeds one Draft
        // RuleSet + 2 rules + 1 decision table per TenantSeedProfiles entry. Default OFF (Development
        // appsettings + the reset-all script enable it). Self-contained — no cross-module deps, no
        // tenant-existence guard (a rule set's TenantId is an unenforced stamp).
        services.Configure<DemoRulesSeederOptions>(
            configuration.GetSection(DemoRulesSeederOptions.SectionName));
        var demoRulesOpts = configuration
            .GetSection(DemoRulesSeederOptions.SectionName)
            .Get<DemoRulesSeederOptions>();
        if (demoRulesOpts?.Enabled == true)
        {
            services.AddHostedService<DemoRulesSeeder>();
        }

        return services;
    }
}
