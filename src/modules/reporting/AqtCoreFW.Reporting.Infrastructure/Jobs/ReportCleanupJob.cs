using AqtCoreFW.Reporting.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Reporting.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job — cleans up expired report execution files.
/// Deletes physical files from disk and clears ResultFilePath on the execution record.
/// E-06: Prevents unbounded disk growth from generated report files.
/// Default retention: 7 days (files older than cutoff are eligible for cleanup).
/// Register via RecurringJob.AddOrUpdate in InfrastructureRegistration or app startup.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 300)]
public sealed class ReportCleanupJob(
    IReportExecutionRepository executionRepository,
    ILogger<ReportCleanupJob> logger)
{
    // Files completed more than this many days ago are eligible for cleanup
    private const int RetentionDays = 7;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);

        var expired = await executionRepository.GetExpiredAsync(cutoff, cancellationToken);

        if (expired.Count == 0)
        {
            logger.LogDebug("ReportCleanupJob: no expired executions found (cutoff={Cutoff})", cutoff);
            return;
        }

        var deleted = 0;
        var failed = 0;

        foreach (var execution in expired)
        {
            if (string.IsNullOrEmpty(execution.ResultFilePath))
                continue;

            try
            {
                if (File.Exists(execution.ResultFilePath))
                    File.Delete(execution.ResultFilePath);

                execution.ClearResultFile();
                deleted++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "ReportCleanupJob: failed to delete file {FilePath} for execution {ExecutionId}",
                    execution.ResultFilePath, execution.Id);
                failed++;
            }
        }

        await executionRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "ReportCleanupJob: cleaned {Deleted} report files, {Failed} failures (cutoff={Cutoff})",
            deleted, failed, cutoff);
    }
}
