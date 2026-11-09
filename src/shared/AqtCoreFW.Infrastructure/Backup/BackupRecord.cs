using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Infrastructure.Backup;

/// <summary>
/// Records each backup/restore-drill operation for NĐ53 compliance audit trail.
/// Stored in [backup].[BackupRecords] table.
/// </summary>
public sealed class BackupRecord
{
    public Guid Id { get; private set; }

    /// <summary>"Backup" or "RestoreDrill"</summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>"Pending", "InProgress", "Completed", "Failed"</summary>
    public string Status { get; private set; } = "Pending";

    /// <summary>Backup file path on server (e.g. /backups/aqtcorefw_20260319.bak)</summary>
    public string? FilePath { get; private set; }

    /// <summary>File size in bytes (null if failed)</summary>
    public long? FileSizeBytes { get; private set; }

    /// <summary>Error message if failed</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>User who triggered the operation</summary>
    public Guid TriggeredBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }

    private BackupRecord() { }

    /// <remarks>Callers in src/host/** (BackupAdminController) must pass IGuidGenerator — host build will show this breakage until B6.</remarks>
    public static BackupRecord Create(IGuidGenerator guidGen, string type, Guid triggeredBy) =>
        new() { Id = guidGen.NewIdMonotonic(), Type = type, TriggeredBy = triggeredBy };

    public void MarkInProgress() => Status = "InProgress";

    public void MarkCompleted(string filePath, long fileSizeBytes)
    {
        Status = "Completed";
        FilePath = filePath;
        FileSizeBytes = fileSizeBytes;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = "Failed";
        ErrorMessage = errorMessage;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
