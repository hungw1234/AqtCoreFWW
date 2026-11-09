namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Application-layer abstraction for reading Hangfire job state without
/// taking a direct Hangfire dependency in the Application project.
/// Implemented in Infrastructure using JobStorage.Current.GetMonitoringApi().
/// </summary>
public interface IHangfireJobStateProvider
{
    /// <summary>
    /// Returns the current state name for a Hangfire job (e.g. "Succeeded", "Processing").
    /// Returns null if the job ID is null or the job cannot be found.
    /// </summary>
    string? GetJobState(string? jobId);
}
