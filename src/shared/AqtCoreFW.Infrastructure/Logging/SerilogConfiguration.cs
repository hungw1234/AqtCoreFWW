using Serilog;
using Serilog.Formatting.Json;

namespace AqtCoreFW.Infrastructure.Logging;

/// <summary>
/// Configures Serilog structured logging.
/// Console JSON + daily rolling file. Production level: Warning (configured via appsettings).
/// PII masking applied via LogSanitizingDestructuringPolicy (Phase 05).
/// </summary>
public static class SerilogConfiguration
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((ctx, services, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services)
            // Phase 06 S4a: mask secret-shaped property values in structured logs (QĐ 742 §4.4.a)
            .Destructure.With<SecretMaskingDestructuringPolicy>()
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("Application", "AqtCoreFW")
            .Enrich.WithProperty(
                "Version",
                typeof(SerilogConfiguration).Assembly.GetName().Version?.ToString() ?? "unknown")
            .WriteTo.Console(new JsonFormatter(renderMessage: true))
            .WriteTo.File(
                new JsonFormatter(),
                path: "logs/aqtcorefw-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 100_000_000));

        return builder;
    }
}
