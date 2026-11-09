using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace AqtCoreFW.Infrastructure.HealthChecks;

/// <summary>Lightweight HTTP-GET health check used for AI service probes (Qdrant, Ollama).</summary>
internal sealed class HttpGetHealthCheck(string url, TimeSpan timeout) : IHealthCheck
{
    private static readonly HttpClient SharedClient = new();

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var response = await SharedClient.GetAsync(url, cts.Token);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded($"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(ex.Message);
        }
    }
}

/// <summary>
/// Health check registration and endpoint mapping.
/// /health/live  — K8s liveness probe (no dependency checks, pod is running).
/// /health/ready — K8s readiness probe (SQL Server + Redis connectivity, optional AI and broker
///                 probes, and audit read-isolation posture). Degraded checks keep the pod in
///                 rotation (HTTP 200).
/// </summary>
public static class HealthCheckConfiguration
{
    public static IServiceCollection AddGovHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("Default")
            ?? configuration["Database:ConnectionString"]
            ?? string.Empty;
        var redisConnStr = configuration["Redis:ConnectionString"] ?? "localhost:6379";

        var checks = services.AddHealthChecks()
            .AddSqlServer(
                connectionString: connStr,
                name: "sqlserver",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["db", "ready"])
            .AddRedis(
                redisConnectionString: redisConnStr,
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: ["cache", "ready"])
            // Surfaces a dormant audit read-isolation boundary (NonAudit login unprovisioned in
            // Production) as Degraded — visible on /health/ready without failing the pod. Resolves
            // IConfiguration + IWebHostEnvironment from DI. See NonAuditIsolationHealthCheck.
            .AddCheck<NonAuditIsolationHealthCheck>(
                name: "audit-read-isolation",
                failureStatus: HealthStatus.Degraded,
                tags: ["audit-isolation", "ready"])
            // Surfaces uploads being stored without a virus scan (feature:files.virus-scan off in
            // Production) as Degraded — visible on /health/ready without failing the pod, since
            // scanning-off is the shipped state until clamd is provisioned. Resolves
            // IFeatureFlagService + IWebHostEnvironment from DI. See VirusScanPostureHealthCheck.
            .AddCheck<VirusScanPostureHealthCheck>(
                name: "upload-virus-scan",
                failureStatus: HealthStatus.Degraded,
                tags: ["files", "ready"]);

        // AI infrastructure health checks — only when Ollama is enabled
        var ollamaEnabled = configuration.GetSection("AI:Ollama").GetValue<bool>("Enabled", false);
        if (ollamaEnabled)
        {
            var qdrantBaseUrl = configuration["AI:Qdrant:Endpoint"] ?? "http://localhost:6333";
            var ollamaBaseUrl = configuration["AI:Ollama:Endpoint"] ?? "http://localhost:11434";
            var timeout = TimeSpan.FromSeconds(3);

            checks
                .AddCheck("qdrant",
                    new HttpGetHealthCheck(qdrantBaseUrl + "/readyz", timeout),
                    failureStatus: HealthStatus.Degraded,
                    tags: ["ready"])
                .AddCheck("ollama",
                    new HttpGetHealthCheck(ollamaBaseUrl + "/api/tags", timeout),
                    failureStatus: HealthStatus.Degraded,
                    tags: ["ready"]);
        }

        // Broker probe — only when RabbitMQ is the configured transport. The bus defaults to
        // InMemory (see MessagingRegistration), where a broker probe would report Degraded about
        // a broker the system never connects to. Same conditional shape as the AI probes above.
        // Degraded, not Unhealthy: sends go through the outbox with retry, and most requests never
        // touch the broker, so an outage should not pull the pod out of rotation.
        var transport = configuration.GetValue<string>("Bus:Transport") ?? "InMemory";
        if (transport.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase))
        {
            var probeTimeout = TimeSpan.FromSeconds(3);

            // Connection keys mirror MessagingRegistration.ConfigureRabbitMq so the probe targets
            // the same broker the bus does. Fallbacks use IsNullOrEmpty, not ??: appsettings ships
            // the section with Username/Password as "" (secrets are never committed), and "" ?? x
            // keeps "" — the probe then logged in as user '' and the broker refused it while the
            // bus (whose client treats empty as unset) connected fine. First caught on the
            // 2026-07-23 RabbitMQ cutover.
            static string Or(string? value, string fallback) =>
                string.IsNullOrEmpty(value) ? fallback : value;
            var rabbitFactory = new ConnectionFactory
            {
                HostName = Or(configuration["Bus:RabbitMq:Host"], "localhost"),
                VirtualHost = Or(configuration["Bus:RabbitMq:VHost"], "/"),
                UserName = Or(configuration["Bus:RabbitMq:Username"], "guest"),
                Password = Or(configuration["Bus:RabbitMq:Password"], "guest"),
                // The health check's own timeout cancels the framework's wait but never reaches the
                // connect — it passes no token to the connection factory. Without this a black-holing
                // broker would hold the socket for the 30s default while /health/ready reported at 3s.
                RequestedConnectionTimeout = probeTimeout
            };
            var connectionSource = new ProbeConnectionSource(() => rabbitFactory.CreateConnectionAsync());

            checks.AddRabbitMQ(
                _ => connectionSource.GetAsync(),
                name: "rabbitmq",
                failureStatus: HealthStatus.Degraded,
                tags: ["messaging", "ready"],
                // Bound the probe like the AI probes above.
                timeout: probeTimeout);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapGovHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Root → liveness probe. Browser-friendly + avoids "broken" 404 on /,
        // while keeping /health/live untouched for monitors that hit it directly.
        endpoints.MapGet("/", () => Results.Redirect("/health/live", permanent: false))
                 .ExcludeFromDescription();

        // Liveness: always healthy — pod is alive
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteJsonResponse
        });

        // Readiness: external dependency checks
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = hc => hc.Tags.Contains("ready"),
            ResponseWriter = WriteJsonResponse
        });

        return endpoints;
    }

    private static async Task WriteJsonResponse(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = (int)e.Value.Duration.TotalMilliseconds
            }),
            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds
        };
        await ctx.Response.WriteAsJsonAsync(response);
    }
}
