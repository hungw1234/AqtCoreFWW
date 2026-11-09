using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Application.Commands;
using AqtCoreFW.SystemParams.Application.Services;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Infrastructure.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.SystemParams.Infrastructure;

/// <summary>DI registration for SystemParams module — called once from Program.cs.</summary>
public static class SystemParamsInfrastructureRegistration
{
    public static IServiceCollection AddSystemParamsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<SystemParamsDbContext>((sp, opts) =>
        {
            opts.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "config"));

            opts.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // MediatR handlers — scan Infrastructure assembly (all command/query handlers live here)
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SystemParamsInfrastructureRegistration).Assembly));

        // FluentValidation validators
        services.AddScoped<IValidator<CreateAnnouncementCommand>, CreateAnnouncementCommandValidator>();

        // SystemParamService resolves ICacheService (L1+L2) + IConnectionMultiplexer (pub/sub) from root
        services.AddSingleton<ISystemParamService, SystemParamService>();

        services.AddSingleton<FeatureFlagService>();
        services.AddSingleton<IFeatureFlagService>(sp => sp.GetRequiredService<FeatureFlagService>());
        services.AddSingleton<SharedKernel.Contracts.IFeatureFlagService>(sp => sp.GetRequiredService<FeatureFlagService>());

        services.AddHostedService<FeatureFlagRefreshService>();
        services.AddHostedService(sp =>
            new FeatureFlagSubscriber(
                sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(),
                sp.GetRequiredService<IFeatureFlagService>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FeatureFlagSubscriber>>()));
        services.AddHostedService<SystemParamSeeder>();

        return services;
    }
}
