using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Application.ModuleClients;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.BusinessProcess.Infrastructure.Queries;
using AqtCoreFW.BusinessProcess.Infrastructure.Repositories;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.BusinessProcess.Infrastructure;

/// <summary>
/// Wires BusinessProcess infrastructure: DbContext (schema=businessprocess, per-schema migrations history),
/// shared interceptors (slow-query, soft-delete, outbox, audit, tenant session-context), and Pattern-A repo.
/// </summary>
public static class BusinessProcessInfrastructureRegistration
{
    public static IServiceCollection AddBusinessProcessInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<BusinessProcessDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "businessprocess"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        services.AddScoped<IBusinessProcessRepository, BusinessProcessRepository>();
        services.AddScoped<IBusinessProcessRuntimeQueries, BusinessProcessRuntimeQueries>();

        // BP MVP P7-FU2/FU3: reference-listing client for Forms/Workflow pre-flight guards.
        services.AddScoped<IBusinessProcessQueryClient, InProcessBusinessProcessQueryClient>();

        return services;
    }
}
