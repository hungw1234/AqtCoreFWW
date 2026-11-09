using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job — purges SodViolationLog rows older than 30 days.
///
/// Design decisions:
/// - Tenant-agnostic: Hangfire workers have NO HTTP tenant context; purge deletes by date only.
/// - Batched DELETE: TOP (10000) per batch with explicit transaction to avoid long locks.
/// - Deterministic order: ORDER BY DetectedAt ASC (oldest-first) via CTE pattern.
/// - Raw SQL: ExecuteSqlRawAsync with parameterized cutoff — no EF change tracking overhead.
/// - Idempotent: rerunning same day safely deletes nothing if already purged.
///
/// Observability: logs pending row count before/after purge. No IMetricsRecorder abstraction
/// exists in codebase — log-only fallback per phase spec.
///
/// Cron schedule: 02:00 UTC daily (09:00 Vietnam ICT — off-peak for Asian tenants).
/// </summary>
public sealed class SodAuditLogPurgeJob
{
    private const int BatchSize = 10_000;
    private const int RetentionDays = 30;

    private readonly IdentityDbContext _db;
    private readonly ILogger<SodAuditLogPurgeJob> _logger;

    public SodAuditLogPurgeJob(
        IdentityDbContext db,
        ILogger<SodAuditLogPurgeJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Hangfire entry point — purge all SodViolationLog rows older than 30 days.</summary>
    public async Task ExecuteAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        // Observability: log pending row count BEFORE purge
        var pendingBefore = await _db.SodViolationLogs
            .Where(x => x.CreatedAt < cutoff)
            .CountAsync();

        _logger.LogInformation(
            "SodAuditLogPurgeJob starting — {Pending} rows older than {Cutoff:u}",
            pendingBefore, cutoff);

        if (pendingBefore == 0)
        {
            _logger.LogDebug("SodAuditLogPurgeJob: no rows to purge.");
            return;
        }

        // Raw SQL with BatchSize as literal (const int — no SQL injection risk).
        // Cutoff passed as parameter {0} via ExecuteSqlRawAsync.
        // CTE + TOP pattern ensures deterministic oldest-first deletion.
        // Note: [identity] must be bracket-escaped — `identity` is a T-SQL reserved keyword.
        var sql = $"WITH batch AS (SELECT TOP ({BatchSize}) Id FROM [identity].[SodViolationLogs] WHERE DetectedAt < {{0}} ORDER BY DetectedAt ASC) DELETE FROM [identity].[SodViolationLogs] WHERE Id IN (SELECT Id FROM batch);";

        var total = 0;
        while (true)
        {
            // Explicit transaction per batch for determinism + narrow lock scope
            await using var tx = await _db.Database.BeginTransactionAsync();

            var affected = await _db.Database.ExecuteSqlRawAsync(sql, cutoff);

            await tx.CommitAsync();

            total += affected;
            _logger.LogInformation(
                "SodAuditLogPurgeJob batch deleted {BatchCount}; total so far {Total}",
                affected, total);

            if (affected < BatchSize) break;
        }

        // Observability: log pending row count AFTER purge (should be ~0, may be >0 due to concurrent inserts)
        var pendingAfter = await _db.SodViolationLogs
            .Where(x => x.CreatedAt < cutoff)
            .CountAsync();

        _logger.LogInformation(
            "SodAuditLogPurgeJob done — deleted {Total}, remaining pending {Remaining}",
            total, pendingAfter);
    }
}
