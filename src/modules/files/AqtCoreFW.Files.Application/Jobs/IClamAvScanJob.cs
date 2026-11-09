namespace AqtCoreFW.Files.Application.Jobs;

/// <summary>
/// Hangfire job interface for async ClamAV virus scan (phase 2 of two-phase upload).
/// Scan result: Available (pass) | Rejected (fail, storage object deleted).
/// </summary>
public interface IClamAvScanJob
{
    Task ScanAsync(Guid fileId);
}
