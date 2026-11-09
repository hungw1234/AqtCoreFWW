using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// Admin endpoints for Hangfire background job monitoring.
/// Provides job status overview for the admin dashboard.
/// Requires Admin or SystemAdmin role.
/// </summary>
[Route("api/v1/admin/jobs")]
[Authorize(Policy = PermCodes.System.Config.View)]
[RequiresMfa]
public sealed class JobsAdminController : ApiControllerBase
{
    /// <summary>List jobs with pagination, filterable by status.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public IActionResult List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (Math.Max(page, 1) - 1) * pageSize;
        var monitor = JobStorage.Current.GetMonitoringApi();

        var items = new List<JobSummaryDto>();
        var totalCount = 0;

        // Aggregate counts from all job states
        var stats = monitor.GetStatistics();
        var counts = new Dictionary<string, long>
        {
            ["Enqueued"] = stats.Enqueued,
            ["Processing"] = stats.Processing,
            ["Scheduled"] = stats.Scheduled,
            ["Succeeded"] = stats.Succeeded,
            ["Failed"] = stats.Failed,
        };

        // Add recurring jobs count
        using var connection = JobStorage.Current.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();
        counts["Recurring"] = recurringJobs.Count;

        // If status filter, only fetch that state; otherwise return summary with recurring details
        if (string.IsNullOrEmpty(status))
        {
            // Return recurring jobs as items (most useful for dashboard)
            foreach (var job in recurringJobs.Skip(offset).Take(pageSize))
            {
                items.Add(new JobSummaryDto(
                    Id: job.Id,
                    Name: job.Id,
                    Status: job.LastExecution is not null ? "Recurring" : "Idle",
                    CreatedAt: job.CreatedAt?.ToString("o"),
                    LastExecution: job.LastExecution?.ToString("o"),
                    NextExecution: job.NextExecution?.ToString("o"),
                    Cron: job.Cron));
            }

            // Also include failed jobs (they need attention)
            var failedJobs = monitor.FailedJobs(0, Math.Min((int)stats.Failed, 20));
            foreach (var (key, fj) in failedJobs)
            {
                items.Add(new JobSummaryDto(
                    Id: key,
                    Name: fj.Job?.Type?.Name ?? "Unknown",
                    Status: "Failed",
                    CreatedAt: fj.FailedAt?.ToString("o"),
                    LastExecution: fj.FailedAt?.ToString("o"),
                    NextExecution: null,
                    Cron: null));
            }

            totalCount = recurringJobs.Count + (int)stats.Failed;
        }
        else if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            var failedJobs = monitor.FailedJobs(offset, pageSize);
            totalCount = (int)stats.Failed;
            foreach (var (key, fj) in failedJobs)
            {
                items.Add(new JobSummaryDto(
                    Id: key,
                    Name: fj.Job?.Type?.Name ?? "Unknown",
                    Status: "Failed",
                    CreatedAt: fj.FailedAt?.ToString("o"),
                    LastExecution: fj.FailedAt?.ToString("o"),
                    NextExecution: null,
                    Cron: null));
            }
        }
        else if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            // Pending = Enqueued + Scheduled
            var enqueuedJobs = monitor.EnqueuedJobs("default", offset, pageSize);
            totalCount = (int)(stats.Enqueued + stats.Scheduled);
            foreach (var (key, ej) in enqueuedJobs)
            {
                items.Add(new JobSummaryDto(
                    Id: key,
                    Name: ej.Job?.Type?.Name ?? "Unknown",
                    Status: "Pending",
                    CreatedAt: ej.EnqueuedAt?.ToString("o"),
                    LastExecution: null,
                    NextExecution: null,
                    Cron: null));
            }
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            items,
            totalCount,
            counts,
        }));
    }

    /// <summary>Get aggregated job statistics (for dashboard stat card).</summary>
    [HttpGet("stats")]
    [ProducesResponseType(200)]
    public IActionResult Stats()
    {
        var monitor = JobStorage.Current.GetMonitoringApi();
        var stats = monitor.GetStatistics();

        return Ok(ApiResponse<object>.Ok(new
        {
            enqueued = stats.Enqueued,
            processing = stats.Processing,
            scheduled = stats.Scheduled,
            succeeded = stats.Succeeded,
            failed = stats.Failed,
            pending = stats.Enqueued + stats.Scheduled,
        }));
    }
}

public sealed record JobSummaryDto(
    string Id,
    string Name,
    string Status,
    string? CreatedAt,
    string? LastExecution,
    string? NextExecution,
    string? Cron);
