using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Infrastructure.Extensions;

/// <summary>
/// CORS policy for the public API surface, isolated so it can be unit-tested without booting the
/// whole infrastructure stack.
///
/// The allow-list is a load-bearing contract: every custom request header the SPA sends on a
/// cross-origin call (X-Tenant-Id, X-Correlation-Id, X-Request-Token, If-Match, …) MUST be listed
/// here or the browser blocks the request at the CORS preflight. Server-side tests (WebApplicationFactory,
/// curl) bypass preflight entirely, so a missing header passes every server test yet fails in the
/// browser — GovApiCorsPolicyTests locks the built policy against an independent expected set so a
/// dropped header is caught at build time.
/// </summary>
public static class GovApiCorsExtensions
{
    public const string PolicyName = "GovApiPolicy";

    private static readonly string[] AllowedHeaders =
    [
        "Content-Type", "Authorization", "X-Api-Key", "X-XSRF-TOKEN",
        "X-Correlation-Id", "X-Tenant-Id", "X-Request-Token",
        "Accept", "Accept-Language", "If-Match"
    ];

    private static readonly string[] ExposedHeaders =
    [
        "X-Mfa-Required", "X-Must-Change-Password", "X-Password-Expired", "X-Correlation-Id"
    ];

    private static readonly string[] AllowedMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    private static readonly string[] DefaultOrigins =
        ["http://localhost:3000", "http://localhost:4200", "http://localhost:5173"];

    public static IServiceCollection AddGovApiCors(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(opts =>
        {
            opts.AddPolicy(PolicyName, policy =>
            {
                var origins = configuration
                    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? DefaultOrigins;
                policy.WithOrigins(origins)
                    .WithHeaders(AllowedHeaders)
                    .WithExposedHeaders(ExposedHeaders)
                    .WithMethods(AllowedMethods)
                    .AllowCredentials();
            });
        });
        return services;
    }
}
