using AqtCoreFW.Infrastructure.Security;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Infrastructure.Clients;

/// <summary>
/// Wires the HTTP transport for <see cref="IOrgUnitModuleClient"/>: a typed
/// <see cref="HttpOrgUnitModuleClient"/> whose outbound calls carry tenant / correlation / service-token
/// headers via <see cref="TenantPropagationHandler"/>. Only invoked when
/// <c>OrgUnitModuleClient:Transport=Http</c>; the monolith default (InProcess) never calls this.
/// </summary>
public static class OrgUnitModuleClientRegistration
{
    public static IServiceCollection AddHttpOrgUnitModuleClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var baseAddress = configuration["OrgUnitModuleClient:BaseAddress"]
            ?? throw new InvalidOperationException(
                "OrgUnitModuleClient:BaseAddress is required when OrgUnitModuleClient:Transport=Http.");

        services.AddHttpClient<HttpOrgUnitModuleClient>(c => c.BaseAddress = new Uri(baseAddress))
                .AddHttpMessageHandler<TenantPropagationHandler>();

        // Typed clients register as Transient; the IOrgUnitModuleClient mapping is Scoped to match the
        // in-process client's lifetime (resolving a transient typed client per scope is safe).
        services.AddScoped<IOrgUnitModuleClient>(sp => sp.GetRequiredService<HttpOrgUnitModuleClient>());
        return services;
    }
}
