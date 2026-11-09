using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Backup;

/// <summary>
/// Executes SQL Server BACKUP DATABASE via ADO.NET.
/// Called by Hangfire background job — never from HTTP request thread.
/// NĐ53 compliance: backup files stored on configured path with timestamp.
/// </summary>
public sealed class DatabaseBackupService(
    BackupDbContext db,
    IConfiguration configuration,
    IGuidGenerator guidGen,
    ILogger<DatabaseBackupService> logger)
{
    /// <summary>
    /// Entry point for the daily recurring Hangfire backup job (distinct from the manual
    /// admin-triggered <see cref="ExecuteBackupAsync"/>). Creates its own BackupRecord (no user
    /// context — scheduled), runs the full-database backup, then prunes expired backups.
    /// Disaster-recovery backup — NOT the NĐ53 archive jobs (those cold-store aged rows).
    /// </summary>
    public async Task RunScheduledBackupAsync()
    {
        var record = BackupRecord.Create(guidGen, "ScheduledBackup", Guid.Empty);
        db.BackupRecords.Add(record);
        await db.SaveChangesAsync();

        // Reuse the production backup path (DRY) — same BACKUP DATABASE + status tracking.
        await ExecuteBackupAsync(record.Id);

        // Retention prune must never fail the backup itself.
        await PruneRetentionAsync();
    }

    /// <summary>
    /// Deletes backup records (and their .bak files) whose completion date is older than
    /// the configured retention window. Failures are logged and swallowed — a prune failure
    /// must not fail the backup that just succeeded.
    /// </summary>
    private async Task PruneRetentionAsync()
    {
        try
        {
            var retentionDays = configuration.GetValue("Backup:RetentionDays", 30);

            // Guard against a misconfig (0 / negative) that would set the cutoff at/after "now"
            // and delete the backup we just created. Retention below 1 day is never intended.
            if (retentionDays < 1)
            {
                logger.LogWarning(
                    "Backup retention skipped: Backup:RetentionDays={RetentionDays} is below 1 (misconfig?)",
                    retentionDays);
                return;
            }

            // Only completed backups (manual "Backup" + scheduled "ScheduledBackup") are prune
            // candidates; Failed/Pending/InProgress rows have no CompletedAt and are left intact.
            var candidates = await db.BackupRecords
                .Where(r => (r.Type == "Backup" || r.Type == "ScheduledBackup")
                            && r.Status == "Completed")
                .ToListAsync();

            var expired = BackupRetentionPruner.SelectExpired(
                candidates, DateTimeOffset.UtcNow, retentionDays);

            if (expired.Count == 0) return;

            foreach (var record in expired)
            {
                if (!string.IsNullOrEmpty(record.FilePath) && File.Exists(record.FilePath))
                {
                    try
                    {
                        File.Delete(record.FilePath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
                    {
                        // Leave the row so a later run retries the file delete; keep pruning the rest.
                        logger.LogWarning(ex, "Backup retention: could not delete file {FilePath}", record.FilePath);
                        continue;
                    }
                }

                db.BackupRecords.Remove(record);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Backup retention: pruned {Count} expired backup(s)", expired.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup retention prune failed (backup itself unaffected)");
        }
    }

    /// <summary>Execute full database backup and update BackupRecord status.</summary>
    public async Task ExecuteBackupAsync(Guid recordId)
    {
        var record = await db.BackupRecords.FindAsync(recordId);
        if (record is null) return;

        record.MarkInProgress();
        await db.SaveChangesAsync();

        try
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Connection string 'Default' not configured");

            var builder = new SqlConnectionStringBuilder(connectionString);
            var databaseName = builder.InitialCatalog;
            var backupDir = configuration["Backup:Directory"] ?? "/backups";
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{databaseName}_{timestamp}.bak";
            var filePath = Path.Combine(backupDir, fileName);

            // Ensure backup directory exists
            Directory.CreateDirectory(backupDir);

            // Execute BACKUP DATABASE command
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = $"BACKUP DATABASE [{databaseName}] TO DISK = @FilePath WITH FORMAT, INIT, COMPRESSION";
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@FilePath", filePath);
            command.CommandTimeout = 600; // 10 minutes for large databases

            await command.ExecuteNonQueryAsync();

            // Get file size
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Exists ? fileInfo.Length : 0;

            record.MarkCompleted(filePath, fileSize);
            logger.LogInformation("Backup completed: {FilePath} ({Size} bytes)", filePath, fileSize);
        }
        catch (Exception ex)
        {
            record.MarkFailed(ex.Message);
            logger.LogError(ex, "Backup failed for record {RecordId}", recordId);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Execute restore drill: backup → restore to temp DB → verify → drop temp DB.</summary>
    public async Task ExecuteRestoreDrillAsync(Guid recordId)
    {
        var record = await db.BackupRecords.FindAsync(recordId);
        if (record is null) return;

        record.MarkInProgress();
        await db.SaveChangesAsync();

        // Hoisted above the try so the finally can delete them even if the drill throws partway.
        string? backupFile = null;
        string? dataFile = null;
        string? logFile = null;

        try
        {
            // Step 1: Create a fresh backup first
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Connection string 'Default' not configured");

            var builder = new SqlConnectionStringBuilder(connectionString);
            var databaseName = builder.InitialCatalog;
            var backupDir = configuration["Backup:Directory"] ?? "/backups";
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            backupFile = Path.Combine(backupDir, $"{databaseName}_drill_{timestamp}.bak");
            var tempDbName = $"{databaseName}_RestoreDrill_{timestamp}";

            Directory.CreateDirectory(backupDir);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // Backup
            var backupSql = $"BACKUP DATABASE [{databaseName}] TO DISK = @FilePath WITH FORMAT, INIT, COMPRESSION";
            await using (var cmd = new SqlCommand(backupSql, connection))
            {
                cmd.Parameters.AddWithValue("@FilePath", backupFile);
                cmd.CommandTimeout = 600;
                await cmd.ExecuteNonQueryAsync();
            }

            // Restore to temp DB
            var restoreSql = $@"RESTORE DATABASE [{tempDbName}] FROM DISK = @FilePath
                WITH MOVE '{databaseName}' TO @DataFile,
                     MOVE '{databaseName}_log' TO @LogFile,
                     REPLACE";
            dataFile = Path.Combine(backupDir, $"{tempDbName}.mdf");
            logFile = Path.Combine(backupDir, $"{tempDbName}_log.ldf");

            await using (var cmd = new SqlCommand(restoreSql, connection))
            {
                cmd.Parameters.AddWithValue("@FilePath", backupFile);
                cmd.Parameters.AddWithValue("@DataFile", dataFile);
                cmd.Parameters.AddWithValue("@LogFile", logFile);
                cmd.CommandTimeout = 600;
                await cmd.ExecuteNonQueryAsync();
            }

            // Verify — check table count in restored DB
            var verifySql = $"SELECT COUNT(*) FROM [{tempDbName}].sys.tables";
            await using (var cmd = new SqlCommand(verifySql, connection))
            {
                var tableCount = (int)(await cmd.ExecuteScalarAsync())!;
                logger.LogInformation("Restore drill verified: {TableCount} tables in temp DB", tableCount);
            }

            // Cleanup — drop temp DB
            var dropSql = $"DROP DATABASE [{tempDbName}]";
            await using (var cmd = new SqlCommand(dropSql, connection))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            var fileInfo = new FileInfo(backupFile);
            record.MarkCompleted(backupFile, fileInfo.Exists ? fileInfo.Length : 0);
            logger.LogInformation("Restore drill completed successfully");
        }
        catch (Exception ex)
        {
            record.MarkFailed(ex.Message);
            logger.LogError(ex, "Restore drill failed for record {RecordId}", recordId);
        }
        finally
        {
            // Drill artifacts are throwaway once the temp DB is dropped — delete .bak/.mdf/.ldf so the
            // backup volume does not accumulate drill files. The drill .bak is excluded from retention
            // prune (that query selects only Type "Backup"/"ScheduledBackup"), so nothing else deletes it.
            // Runs on success AND mid-drill failure; unassigned paths are still null → skipped by the helper.
            DrillTempFileCleanup.TryDelete(new[] { backupFile, dataFile, logFile }, logger);
        }

        await db.SaveChangesAsync();
    }
}
