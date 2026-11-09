using AqtCoreFW.Infrastructure.Clients;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Clients;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Seeding;
using AqtCoreFW.Organization.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace AqtCoreFW.Organization.Infrastructure;

/// <summary>DI registration for Organization module — called once from Program.cs.</summary>
public static class OrganizationInfrastructureRegistration
{
    public static IServiceCollection AddOrganizationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<OrgDbContext>((sp, opts) =>
        {
            opts.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organization"));

            opts.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        services.AddScoped<OrgUnitService>();
        services.AddScoped<ITenantOrgContext, JwtOrgContext>();
        AddOrgUnitModuleClient(services, configuration);

        // RT2-15 Phase 04: TimeProvider.System registered as singleton for temporal
        // predicate injection (Q4 ratification). Tests substitute FakeTimeProvider via DI override.
        services.AddSingleton(TimeProvider.System);

        // Seeder options (bindings only — hosted-service registration lives at the host).
        // OrgTreeSeeder must run AFTER Identity's TenantSeeder (needs tenant rows) but BEFORE
        // Identity's TenantUserSeeder (which reads organization.OrgUnits), and TenantAssignmentSeeder
        // must run AFTER TenantUserSeeder (needs identity.AspNetUsers). That interleaving across the
        // two modules can only be expressed at the composition root, so the API host registers all
        // five in explicit order: TenantSeeder → TenantRoleSeeder → OrgTreeSeeder → TenantUserSeeder
        // → TenantAssignmentSeeder.
        services.Configure<OrgTreeSeederOptions>(configuration.GetSection(OrgTreeSeederOptions.SectionName));
        services.Configure<TenantAssignmentSeederOptions>(
            configuration.GetSection(TenantAssignmentSeederOptions.SectionName));

        // MediatR: commands/queries in Application; EF handlers in Infrastructure
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(Application.Commands.CreateOrgUnitCommand).Assembly);
            cfg.RegisterServicesFromAssembly(typeof(OrganizationInfrastructureRegistration).Assembly);
        });
        services.AddValidatorsFromAssembly(typeof(Application.Commands.CreateOrgUnitCommand).Assembly);

        return services;
    }

    /// <summary>
    /// Binds <see cref="IOrgUnitModuleClient"/> to the configured transport. Absent or unparseable
    /// <c>OrgUnitModuleClient:Transport</c> → InProcess (the Dapper client, zero behaviour change);
    /// <c>Http</c> → the shared-Infra <see cref="HttpOrgUnitModuleClient"/> with tenant propagation.
    /// This is the only assembly that can see both the local InProcess client and the shared-Infra Http one.
    /// </summary>
    internal static void AddOrgUnitModuleClient(IServiceCollection services, IConfiguration configuration)
    {
        var transport = Enum.TryParse<OrgUnitClientTransport>(
            configuration["OrgUnitModuleClient:Transport"], ignoreCase: true, out var t)
            ? t
            : OrgUnitClientTransport.InProcess;

        if (transport == OrgUnitClientTransport.Http)
            services.AddHttpOrgUnitModuleClient(configuration);
        else
            services.AddScoped<IOrgUnitModuleClient, InProcessOrgUnitModuleClient>();
    }
}

/// <summary>
/// Reads org_unit_id and tenant_id JWT claims; resolves ancestor hierarchy via
/// OrgUnitService (cache-backed — safe for sync-over-async after first request).
/// </summary>
public sealed class JwtOrgContext(IHttpContextAccessor accessor, OrgUnitService orgService)
    : ITenantOrgContext
{
    private IReadOnlyList<Guid>? _cachedHierarchy;

    public Guid? CurrentOrgUnitId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue("org_unit_id"), out var id)
            ? id : null;

    public bool IsInOrgUnit(Guid orgUnitId) =>
        GetOrgUnitHierarchy().Contains(orgUnitId);

    public IReadOnlyList<Guid> GetOrgUnitHierarchy()
    {
        if (_cachedHierarchy is not null) return _cachedHierarchy;

        if (CurrentOrgUnitId is null)
        {
            _cachedHierarchy = [];
            return _cachedHierarchy;
        }

        var tenantIdStr = accessor.HttpContext?.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantIdStr, out var tenantId))
        {
            _cachedHierarchy = [];
            return _cachedHierarchy;
        }

        _cachedHierarchy = orgService
            .GetAncestorsAsync(CurrentOrgUnitId.Value, tenantId)
            .GetAwaiter()
            .GetResult();

        return _cachedHierarchy;
    }
}
