namespace AqtCoreFW.BackgroundJobs.Integration.Tests.Fixtures;

// ---------------------------------------------------------------------------
// Test-only job classes — registered in DI so Hangfire can resolve them.
// Each job signals completion via a static TaskCompletionSource so tests
// can await execution without polling or Thread.Sleep.
// ---------------------------------------------------------------------------

/// <summary>Fire-and-forget test job — signals on first execution.</summary>
public sealed class TestJob
{
    private static TaskCompletionSource<DateTimeOffset> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Reset() =>
        _tcs = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);

    public static Task<DateTimeOffset> WaitForExecutionAsync(TimeSpan timeout) =>
        _tcs.Task.WaitAsync(timeout);

    /// <summary>Called by Hangfire worker — records execution time and signals TCS.</summary>
    public Task ExecuteAsync()
    {
        _tcs.TrySetResult(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}

/// <summary>Scheduled (delayed) test job — signals on first execution.</summary>
public sealed class TestScheduledJob
{
    private static TaskCompletionSource<DateTimeOffset> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Reset() =>
        _tcs = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);

    public static Task<DateTimeOffset> WaitForExecutionAsync(TimeSpan timeout) =>
        _tcs.Task.WaitAsync(timeout);

    public Task ExecuteAsync()
    {
        _tcs.TrySetResult(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}

/// <summary>Recurring test job — counts executions and signals on first.</summary>
public sealed class TestRecurringJob
{
    private static TaskCompletionSource<DateTimeOffset> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Reset() =>
        _tcs = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);

    public static Task<DateTimeOffset> WaitForExecutionAsync(TimeSpan timeout) =>
        _tcs.Task.WaitAsync(timeout);

    public Task ExecuteAsync()
    {
        _tcs.TrySetResult(DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}
