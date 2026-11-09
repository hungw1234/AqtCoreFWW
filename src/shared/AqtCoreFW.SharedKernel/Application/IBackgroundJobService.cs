using System.Linq.Expressions;

namespace AqtCoreFW.SharedKernel.Application;

/// <summary>
/// Background job abstraction — modules call this interface, not Hangfire directly.
/// Enables testing and future job system swap without module changes.
/// Hangfire implementation: HangfireBackgroundJobService (Infrastructure).
/// </summary>
public interface IBackgroundJobService
{
    /// <summary>Fire-and-forget: executes ASAP, retries 3x on failure.</summary>
    string Enqueue<T>(Expression<Action<T>> methodCall);

    /// <summary>Async fire-and-forget: for async methods.</summary>
    string Enqueue<T>(Expression<Func<T, Task>> methodCall);

    /// <summary>
    /// Async fire-and-forget on an explicit named queue. The processing server MUST list this queue
    /// (see HangfireRegistration), or the job is enqueued where no worker listens and never runs.
    /// Use the constants in <see cref="BackgroundJobQueues"/> rather than raw strings.
    /// </summary>
    string Enqueue<T>(string queue, Expression<Func<T, Task>> methodCall);

    /// <summary>Scheduled: executes once after the specified delay.</summary>
    string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay);

    /// <summary>Recurring: registers or updates a cron-scheduled job.</summary>
    void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression);

    /// <summary>Continuation: jobB starts after jobA completes successfully.</summary>
    string ContinueJobWith<T>(string parentJobId, Expression<Action<T>> methodCall);

    /// <summary>Delete a recurring job by ID.</summary>
    void RemoveRecurring(string jobId);
}
