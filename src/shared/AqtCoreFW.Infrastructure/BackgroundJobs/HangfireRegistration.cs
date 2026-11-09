using AqtCoreFW.Infrastructure.Backup;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Archive;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Infrastructure.BackgroundJobs;

/// <summary>
/// Registers Hangfire with SQL Server storage (hangfire schema).
/// Queue priority order: webhook-critical → webhook-normal → audit → virus-scan → background → default.
/// Brainstorm decision: webhook 1s, audit 2s, background 15s polling intervals handled
/// by Hangfire's internal dispatcher per queue priority — no manual polling config needed.
/// </summary>
public static class HangfireRegistration
{
    public static IServiceCollection AddHangfireJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is required for Hangfire SQL Server storage.");

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseFilter(new PiiMaskingJobFilter())
            .UseSqlServerStorage(connStr, new SqlServerStorageOptions
            {
                SchemaName = "hangfire",
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero, // use SQL Server long-polling (immediate)
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true           // avoids distributed lock table contention
            }));

        services.AddHangfireServer(opt =>
        {
            // Cap at 20 workers to prevent thread pool exhaustion under load
            opt.WorkerCount = Math.Min(Environment.ProcessorCount * 5, 20);
            // virus-scan sits above background/default so file scans aren't starved by generic background work.
            // BackgroundJobQueues.VirusScan is the SAME constant the upload handler enqueues with — they must
            // match or scan jobs land on a queue no worker drains and stay stuck.
            opt.Queues = ["webhook-critical", "webhook-normal", BackgroundJobQueues.Audit, BackgroundJobQueues.VirusScan, "background", "default"];
            opt.SchedulePollingInterval = TimeSpan.FromSeconds(2);
            opt.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        // Job abstractions — Scoped so job classes resolve Scoped services via DI
        services.AddScoped<IBackgroundJobService, HangfireBackgroundJobService>();
        services.AddScoped<IArchiveService, SqlServerArchiveService>();

        return services;
    }

    /// <summary>
    /// Registers NĐ53 recurring archive jobs.
    /// Called after app.Build() when IApplicationBuilder is available and DI is sealed.
    /// Hangfire stores job definitions in SQL — safe to call on every startup (idempotent).
    /// </summary>
    public static void RegisterRecurringJobs()
    {
        // NĐ53: audit logs — 12-month hot retention, archive on 1st of each month at 03:00
        RecurringJob.AddOrUpdate<IArchiveService>(
            "archive-audit",
            queue: "background",
            methodCall: svc => svc.ArchiveRecordsOlderThanAsync(
                "audit", TimeSpan.FromDays(365), CancellationToken.None),
            cronExpression: "0 3 1 * *");

        // NĐ53: cases — 24-month hot retention
        RecurringJob.AddOrUpdate<IArchiveService>(
            "archive-cases",
            queue: "background",
            methodCall: svc => svc.ArchiveRecordsOlderThanAsync(
                "cases", TimeSpan.FromDays(730), CancellationToken.None),
            cronExpression: "0 3 1 * *");

        // Disaster-recovery: full-database backup, daily at 02:00 (an hour before the archive
        // window). This is NOT an NĐ53 archive job — archive jobs cold-store aged rows for
        // compliance retention; this takes a full-DB restore point. Retention window is
        // config-driven (Backup:RetentionDays); the cron is a code literal (rarely changes).
        // See docs/ops/backup-restore-runbook.md.
        RecurringJob.AddOrUpdate<DatabaseBackupService>(
            "scheduled-backup",
            queue: "background",
            methodCall: svc => svc.RunScheduledBackupAsync(),
            cronExpression: "0 2 * * *");
    }
}
