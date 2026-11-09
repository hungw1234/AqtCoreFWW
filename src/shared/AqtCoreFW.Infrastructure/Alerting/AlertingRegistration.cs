using AqtCoreFW.Infrastructure.Configuration;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Infrastructure.Alerting;

/// <summary>
/// DI registration and Hangfire recurring job setup for the alerting subsystem (M14).
/// Call AddAlertingInfrastructure() in Program.cs after AddInfrastructure().
/// Call RegisterRecurringJobs() after app.Build() alongside other recurring job registrations.
/// </summary>
public static class AlertingRegistration
{
    /// <summary>
    /// Registers AlertingDbContext and AlertEvaluationJob with DI.
    /// Prometheus base URL is read from configuration key "Prometheus:BaseUrl"
    /// (default: http://localhost:9090 for local dev).
    /// </summary>
    public static IServiceCollection AddAlertingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = NonAuditConnectionString.Get(configuration)
            ?? throw new InvalidOperationException(
                "ConnectionStrings:NonAudit or ConnectionStrings:Default is required for AlertingDbContext.");

        services.AddDbContext<AlertingDbContext>(opts =>
            opts.UseSqlServer(connStr, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "alerting")));

        // Named HttpClient for Prometheus — base address configurable per environment
        var prometheusBase = configuration["Prometheus:BaseUrl"] ?? "http://localhost:9090/";
        services.AddHttpClient("prometheus", client =>
        {
            client.BaseAddress = new Uri(prometheusBase);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // AlertEvaluationJob resolved from DI per Hangfire execution
        services.AddScoped<AlertEvaluationJob>();

        // Idempotent startup seeder for disabled dev AlertRules — no-op unless Alerting:DevSeed:Enabled=true.
        services.AddHostedService<AlertRuleDevSeeder>();

        return services;
    }

    /// <summary>
    /// Registers the recurring alert evaluation job — every 1 minute, UTC.
    /// Call after app.Build() so Hangfire storage is initialised.
    /// Safe to call on every startup (idempotent AddOrUpdate).
    /// </summary>
    public static void RegisterRecurringJobs()
    {
        RecurringJob.AddOrUpdate<AlertEvaluationJob>(
            recurringJobId: "alert-evaluation",
            queue: "background",
            methodCall: job => job.ExecuteAsync(CancellationToken.None),
            cronExpression: "* * * * *",          // every 1 minute
            options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }
}
