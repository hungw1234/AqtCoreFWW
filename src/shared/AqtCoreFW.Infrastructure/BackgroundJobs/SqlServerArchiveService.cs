using AqtCoreFW.SharedKernel.Application.Archive;
using AqtCoreFW.SharedKernel.Application.Data;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.BackgroundJobs;

/// <summary>
/// Archives aged records per NĐ53 compliance (12-month hot, 24-month archive, then purge).
/// Strategy: INSERT INTO [archive].[{module}_records] SELECT ... + soft-delete source rows.
/// Retention periods are caller-supplied — not hardcoded here (open/closed principle).
/// Real per-module SQL added when each module is implemented (Phase 07+).
/// </summary>
public sealed class SqlServerArchiveService(
    IReadDbConnection db,
    ILogger<SqlServerArchiveService> logger) : IArchiveService
{
    public async Task ArchiveRecordsOlderThanAsync(
        string moduleName,
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTimeOffset.UtcNow - retentionPeriod;

        logger.LogInformation(
            "Starting archive for module {Module}, cutoff: {CutoffDate}",
            moduleName, cutoffDate);

        // Pattern: each module registers its archive SQL via IArchiveStrategy (Phase 07+).
        // Stub executes a no-op query so the Hangfire job succeeds during framework setup.
        var count = await db.ExecuteAsync(
            "SELECT 1 WHERE 1 = 0",
            new { moduleName, cutoffDate },
            cancellationToken);

        logger.LogInformation(
            "Archive complete for module {Module}: {Count} records archived",
            moduleName, count);
    }

    public async Task<ArchiveStatus> GetArchiveStatusAsync(
        string moduleName,
        CancellationToken cancellationToken = default)
    {
        // Check whether an archive schema exists for this module
        var archiveTableCount = await db.QueryFirstOrDefaultAsync<long?>(
            "SELECT COUNT(1) FROM information_schema.tables WHERE table_schema = @schema",
            new { schema = $"{moduleName}_archive" },
            cancellationToken);

        return new ArchiveStatus(
            ModuleName: moduleName,
            TotalRecords: 0,
            ArchivedRecords: archiveTableCount ?? 0,
            LastArchivedAt: null,
            Status: "available");
    }
}
