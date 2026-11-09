using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

namespace AqtCoreFW.Infrastructure.Telemetry;

/// <summary>
/// OpenTelemetry configuration — traces + metrics via OTLP exporter.
/// ActivitySource "AqtCoreFW" for manual spans in command/query handlers.
///
/// <para><b>Two scrape endpoints serve two separate metric pipelines; a collector must scrape BOTH
/// or it silently receives half the metrics.</b> They are kept apart because each writes a complete
/// Prometheus exposition body and neither can share a route with the other:</para>
/// <list type="bullet">
/// <item><c>/metrics</c> — prometheus-net (<see cref="UsePrometheusMetrics"/> + <c>MapMetrics</c>).
/// Serves the HTTP request metrics (<c>http_requests_received_total</c>,
/// <c>http_request_duration_seconds_bucket</c>) that <c>infra/prometheus/recording-rules.yml</c> and the
/// Grafana dashboards query by name.</item>
/// <item><c>/metrics-otel</c> — this pipeline (<see cref="MapOtelMetricsEndpoint"/>). The Prometheus
/// exporter attaches to the whole MeterProvider, so this serves <b>everything</b> registered below:
/// the module counters and histograms from each <c>AddMeter</c>, and also the ASP.NET Core,
/// HttpClient and runtime instrumentation. That means HTTP request timings are collected twice
/// across the two endpoints under different names — prometheus-net's
/// <c>http_request_duration_seconds_bucket</c> here versus OTel's semconv
/// <c>http_server_request_duration_seconds_bucket</c> there. They do not collide, and only the
/// prometheus-net names are queried by the recording rules and dashboards; narrow this with a view
/// if the duplicate ever costs more than it is worth.
///
/// Without this endpoint a module counter is reachable only through the OTLP collector — and never
/// by <c>AlertEvaluationJob</c>, which alerts by querying Prometheus over HTTP.</item>
/// </list>
/// </summary>
public static class OpenTelemetryConfiguration
{
    /// <summary>Shared ActivitySource for manual tracing in handlers and services.</summary>
    public static readonly System.Diagnostics.ActivitySource AqtSource = new("AqtCoreFW", "1.0.0");

    public static IServiceCollection AddOpenTelemetryObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Otel:Endpoint"] ?? "http://localhost:4317";
        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Development";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService("AqtCoreFW", serviceVersion: "1.0.0")
                .AddAttributes([new("deployment.environment", environment)]))
            .WithTracing(tracing => tracing
                .AddSource("AqtCoreFW")
                .AddAspNetCoreInstrumentation(opt => opt.RecordException = true)
                .AddHttpClientInstrumentation()
                // EF Core 1.13+ removed SetDbStatementForText (SQL emission now always-on by default).
                // Enricher strips db.statement + db.query.text post-capture — multi-tenant prod must
                // not leak sanitized SQL shape to shared OTLP collector. See multi-tenant-deployment.md.
                .AddEntityFrameworkCoreInstrumentation(opt => opt.EnrichWithIDbCommand = (activity, _) =>
                {
                    activity.SetTag("db.statement", null);
                    activity.SetTag("db.query.text", null);
                })
                .AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Module Meters (BP MVP P0 — Workflow perf histograms; BP MVP P6 — BP prefill latency;
                // Collaboration — realtime SignalR broadcast-failure counter).
                .AddMeter("AqtCoreFW.Workflow")
                .AddMeter("AqtCoreFW.BusinessProcess")
                .AddMeter("AqtCoreFW.Collaboration")
                // Identity — signing provider-allowlist deny counter (rate-based AlertRule source).
                .AddMeter("AqtCoreFW.Identity")
                // Audit — ingestion durability counters (fallback_enqueued/recovered/lost; lost = alert source).
                .AddMeter("AqtCoreFW.Audit")
                // Forms — UX-baseline counters (submitted/opened → completion-rate; validation errors/submit histogram).
                .AddMeter("AqtCoreFW.Forms")
                // Alerting — the alert job's own delivery/evaluation failures, so a silent alerting
                // outage is itself alertable (see AlertingMetrics).
                .AddMeter(Alerting.AlertingMetrics.MeterName)
                .AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint))
                // Both exporters read the same MeterProvider. OTLP feeds the collector; the Prometheus
                // exporter backs the /metrics-otel scrape endpoint that alert rules query.
                .AddPrometheusExporter());

        return services;
    }

    /// <summary>
    /// Enables prometheus-net HTTP metrics middleware.
    /// Call before UseRouting so all routes are captured.
    /// </summary>
    public static IApplicationBuilder UsePrometheusMetrics(this IApplicationBuilder app)
    {
        app.UseHttpMetrics();
        return app;
    }

    /// <summary>
    /// Maps the scrape endpoint exposing the <c>AddMeter</c>-registered instruments.
    /// </summary>
    /// <remarks>
    /// The path is deliberately NOT <c>/metrics</c>: prometheus-net already owns that route, and both
    /// this endpoint and that one are terminal handlers writing a full exposition body. Sharing the
    /// route would let whichever registered first answer every scrape while the other's series vanish
    /// with no error — silent, order-dependent metric loss.
    /// </remarks>
    public static IEndpointRouteBuilder MapOtelMetricsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPrometheusScrapingEndpoint("/metrics-otel");
        return endpoints;
    }
}
