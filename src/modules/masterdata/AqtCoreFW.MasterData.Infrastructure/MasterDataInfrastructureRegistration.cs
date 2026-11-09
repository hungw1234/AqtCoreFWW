using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Application.Services;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Infrastructure.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.MasterData.Infrastructure;

/// <summary>DI registration for MasterData module — called once from Program.cs.</summary>
public static class MasterDataInfrastructureRegistration
{
    public static IServiceCollection AddMasterDataInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MediatR: commands/queries in Application; EF handlers in Infrastructure
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(Application.Commands.CreateDictionary.CreateDictionaryCommand).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(MasterDataInfrastructureRegistration).Assembly);
        });
        services.AddValidatorsFromAssembly(typeof(Application.Commands.CreateDictionary.CreateDictionaryCommand).Assembly);

        // DbContext — uses shared interceptors from InfrastructureRegistration
        services.AddDbContext<MasterDataDbContext>((sp, opts) =>
        {
            opts.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "masterdata"));

            opts.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // AdministrativeUnitService — singleton (in-memory map, BuildCacheAsync called by seeder)
        services.AddSingleton<AdministrativeUnitService>();
        services.AddSingleton<IAdministrativeUnitService>(
            sp => sp.GetRequiredService<AdministrativeUnitService>());

        // Startup seeder
        services.AddHostedService<MasterDataSeeder>();

        // OutputCache policies
        services.AddOutputCache(opts =>
        {
            opts.AddPolicy("MasterData", b => b
                .Expire(TimeSpan.FromMinutes(30))
                .Tag("masterdata"));

            opts.AddPolicy("AdminUnits", b => b
                .Expire(TimeSpan.FromMinutes(30))
                .Tag("admin-units")
                .SetVaryByQuery("level", "parentCode"));
        });

        return services;
    }
}
