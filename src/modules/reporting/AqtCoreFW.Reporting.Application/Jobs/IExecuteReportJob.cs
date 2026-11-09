namespace AqtCoreFW.Reporting.Application.Jobs;

/// <summary>
/// Hangfire job interface for executing a queued report.
/// Interface allows IBackgroundJobService.Enqueue{T} to reference the job without
/// taking a direct dependency on the Infrastructure implementation.
/// </summary>
public interface IExecuteReportJob
{
    Task ExecuteAsync(Guid executionId, CancellationToken cancellationToken);
}
