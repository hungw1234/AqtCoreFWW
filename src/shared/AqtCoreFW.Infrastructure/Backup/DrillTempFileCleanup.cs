using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Backup;

/// <summary>
/// Deletes the throwaway temp files a restore drill leaves in the backup directory
/// (.bak / .mdf / .ldf). Extracted from <see cref="DatabaseBackupService"/> so the
/// swallow-and-log behavior is unit-testable without running a real drill.
///
/// A restore drill re-creates a fresh <c>FORMAT, INIT</c> backup purely to feed the restore
/// verification, then drops the temp DB — none of its three files are a recovery source, and
/// the drill's <c>.bak</c> record is excluded from retention prune (the pruner query selects
/// only <c>Type == "Backup"/"ScheduledBackup"</c>), so without this cleanup the backup volume
/// accumulates drill artifacts indefinitely. A locked or missing file is logged and skipped —
/// cleanup runs in the drill's <c>finally</c> and must never fail the drill it follows.
///
/// Known limit: if a drill fails AFTER restore but BEFORE the temp DB is dropped, SQL Server still
/// holds the <c>.mdf</c>/<c>.ldf</c> OS handles, so their delete is swallowed and they remain until a
/// later run — a rare, logged, bounded case in the failure branch only (the success path and every
/// <c>.bak</c> are always cleaned). Closing it would require dropping the temp DB in its own finally.
/// </summary>
public static class DrillTempFileCleanup
{
    /// <summary>
    /// Best-effort delete of each non-null, existing path. Null/empty/missing paths are skipped;
    /// a known-safe I/O failure (locked file, permissions) is logged as a warning and never thrown.
    /// </summary>
    public static void TryDelete(IEnumerable<string?> filePaths, ILogger logger)
    {
        foreach (var path in filePaths)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
            {
                logger.LogWarning(ex, "Restore drill cleanup: could not delete temp file {FilePath}", path);
            }
        }
    }
}
