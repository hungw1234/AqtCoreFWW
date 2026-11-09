namespace AqtCoreFW.Infrastructure.Backup;

/// <summary>
/// Pure retention-selection logic for database backups, extracted from
/// <see cref="DatabaseBackupService"/> so the cutoff boundary is unit-testable without
/// running a real BACKUP DATABASE.
/// </summary>
public static class BackupRetentionPruner
{
    /// <summary>
    /// Selects the backup records whose completion timestamp is strictly older than the
    /// retention cutoff (<paramref name="now"/> minus <paramref name="retentionDays"/>).
    /// Records with no <see cref="BackupRecord.CompletedAt"/> (Pending/InProgress/Failed)
    /// are never selected.
    /// </summary>
    public static IReadOnlyList<BackupRecord> SelectExpired(
        IEnumerable<BackupRecord> records, DateTimeOffset now, int retentionDays)
    {
        var cutoff = now - TimeSpan.FromDays(retentionDays);
        return records
            .Where(r => r.CompletedAt is { } completedAt && completedAt < cutoff)
            .ToList();
    }
}
