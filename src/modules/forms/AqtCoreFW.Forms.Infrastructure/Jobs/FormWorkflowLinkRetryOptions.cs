namespace AqtCoreFW.Forms.Infrastructure.Jobs;

/// <summary>
/// Retry knobs for <see cref="FormWorkflowLinkRetryJob"/>, bound from config section
/// <c>Forms:WorkflowLinkRetry</c>. Defaults are the LOCKED launch values (plan Q1, ratified
/// 2026-08-03) — exponential backoff 5 minutes → 6-hour cap, scanned every 5 minutes.
/// </summary>
public sealed class FormWorkflowLinkRetryOptions
{
    public const string SectionName = "Forms:WorkflowLinkRetry";

    /// <summary>Attempts allowed before a row is shelved (dead-lettered). LOCKED = 10.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Max rows claimed per job tick. LOCKED = 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Hangfire recurring-job cron expression. LOCKED = every 5 minutes.</summary>
    public string Cron { get; set; } = "*/5 * * * *";

    /// <summary>Backoff base for attempt 0 (doubles each subsequent attempt). LOCKED = 5 minutes.</summary>
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Backoff cap — exponential growth never exceeds this. LOCKED = 6 hours.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromHours(6);
}
