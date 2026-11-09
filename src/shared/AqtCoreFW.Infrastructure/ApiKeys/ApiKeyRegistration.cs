using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Infrastructure.ApiKeys;

/// <summary>
/// DI registration for API key subsystem (DbContext, service, auth handler).
/// Called from InfrastructureRegistration or GatewayRegistration.
/// </summary>
public static class ApiKeyRegistration
{
    public static IServiceCollection AddApiKeyAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = NonAuditConnectionString.Get(configuration)
            ?? throw new InvalidOperationException(
                "ConnectionStrings:NonAudit or ConnectionStrings:Default is required for ApiKeyDbContext.");

        services.AddDbContext<ApiKeyDbContext>(opts =>
            opts.UseSqlServer(connStr,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "gateway")));

        services.AddScoped<ApiKeyService>();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>(
                ApiKeyAuthHandler.SchemeName, _ => { });

        // Test key seeder — no-op unless SEED_TEST_APIKEY=true env var is set
        services.AddHostedService<ApiKeyTestSeeder>();

        return services;
    }
}
