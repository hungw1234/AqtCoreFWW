namespace AqtCoreFW.SharedKernel.Application.Archive;

/// <summary>
/// Archive service for NĐ53 compliance — moves aged records to cold storage.
/// Called by Hangfire recurring jobs. Implementation in Infrastructure.
/// Retention periods: audit 12 months hot, cases 24 months hot, then archive.
/// </summary>
public interface IArchiveService
{
    /// <summary>Archives records from the specified module older than retention period.</summary>
    Task ArchiveRecordsOlderThanAsync(
        string moduleName,
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default);

    Task<ArchiveStatus> GetArchiveStatusAsync(string moduleName, CancellationToken cancellationToken = default);
}

public sealed record ArchiveStatus(
    string ModuleName,
    long TotalRecords,
    long ArchivedRecords,
    DateTimeOffset? LastArchivedAt,
    string Status);
