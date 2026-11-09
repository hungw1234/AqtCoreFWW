using AqtCoreFW.Workflow.Application.Services;
using Hangfire;

namespace AqtCoreFW.Workflow.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of IHangfireJobStateProvider.
/// Reads job state from Hangfire's monitoring API (JobStorage.Current).
/// Null-safe: returns null for missing job IDs or unknown jobs.
/// </summary>
public sealed class HangfireJobStateProvider : IHangfireJobStateProvider
{
    /// <inheritdoc/>
    public string? GetJobState(string? jobId)
    {
        if (string.IsNullOrEmpty(jobId)) return null;

        try
        {
            var monitor = JobStorage.Current.GetMonitoringApi();
            var details = monitor.JobDetails(jobId);
            return details?.History.FirstOrDefault()?.StateName;
        }
        catch
        {
            // JobStorage may not be initialized in test environments — return null gracefully
            return null;
        }
    }
}
