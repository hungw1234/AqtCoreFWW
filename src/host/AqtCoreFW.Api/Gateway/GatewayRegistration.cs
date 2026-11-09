using Asp.Versioning;
using Scalar.AspNetCore;

namespace AqtCoreFW.Api.Gateway;

/// <summary>
/// Gateway service registration — YARP reverse proxy (Mode A: in-process, Mode B: standalone),
/// API versioning (Asp.Versioning.Mvc URL-segment), OpenAPI .NET 10, Scalar UI.
/// Switch via appsettings: Gateway:Mode = "InProcess" | "Standalone".
/// </summary>
public static class GatewayRegistration
{
    public static IServiceCollection AddGateway(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- API Versioning (URL segment: /api/v1/) ---
        services.AddApiVersioning(o =>
        {
            o.DefaultApiVersion = new ApiVersion(1, 0);
            o.AssumeDefaultVersionWhenUnspecified = true;
            o.ReportApiVersions = true;
            o.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddMvc()
        .AddApiExplorer(o =>
        {
            o.GroupNameFormat = "'v'VVV";
            o.SubstituteApiVersionInUrl = true;
        });

        // --- OpenAPI (.NET 10 built-in) ---
        // NOTE: Enums serialize as strings at runtime (JsonStringEnumConverter in Program.cs)
        // but OpenAPI schema still shows "integer". Orval types may need manual enum overrides.
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer<OpenApiVietnameseTransformer>();
        });

        // --- YARP (Mode A — in-process loopback) ---
        var gatewayMode = configuration["Gateway:Mode"] ?? "InProcess";
        if (gatewayMode.Equals("InProcess", StringComparison.OrdinalIgnoreCase))
        {
            services.AddReverseProxy()
                .LoadFromConfig(configuration.GetSection("ReverseProxy"))
                .ConfigureHttpClient((_, handler) =>
                {
                    // ForwarderHttpClient tuning (S6): HTTP/2 multiplexing + connection pool
                    handler.MaxConnectionsPerServer = 100;
                    handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2);
                });
        }

        return services;
    }

    public static WebApplication UseGateway(this WebApplication app)
    {
        // OpenAPI endpoint — always available (consumed by DAST scanners in CI)
        app.MapOpenApi();

        // Scalar UI — dev/staging only (disabled in production to hide API surface)
        if (!app.Environment.IsProduction())
            app.MapScalarApiReference("/scalar/v1");

        // YARP — map after auth middleware
        var gatewayMode = app.Configuration["Gateway:Mode"] ?? "InProcess";
        if (gatewayMode.Equals("InProcess", StringComparison.OrdinalIgnoreCase))
            app.MapReverseProxy();

        return app;
    }
}
