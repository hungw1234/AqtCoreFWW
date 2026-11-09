using AqtCoreFW.BackgroundJobs.Integration.Tests.Fixtures;
using Hangfire;

namespace AqtCoreFW.BackgroundJobs.Integration.Tests;

/// <summary>
/// Integration tests for HangfireBackgroundJobService against InMemory Hangfire storage.
/// TC-JOB-INT-001 through TC-JOB-INT-003.
/// Single BackgroundJobServer with 1 worker runs inside the test process — no Docker required.
/// All tests share one server instance via HangfireCollection (cold-start once per session).
/// </summary>
[Collection(HangfireCollection.CollectionName)]
public sealed class HangfireBackgroundJobServiceTests(HangfireFixture fixture)
{
    // Generous timeout — InMemory Hangfire with polling=100ms should execute in < 2s
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(10);

    // -------------------------------------------------------------------------
    // TC-JOB-INT-001: EnqueueAsync schedules and executes a fire-and-forget job
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-JOB-INT-001")]
    public async Task Enqueue_FireAndForget_JobExecutesSuccessfully()
    {
        TestJob.Reset();

        var enqueuedAt = DateTimeOffset.UtcNow;

        // Enqueue via BackgroundJobClient (Hangfire static API under the hood)
        fixture.JobClient.Enqueue<TestJob>(j => j.ExecuteAsync());

        // Wait for the Hangfire worker to pick up and execute the job
        var executedAt = await TestJob.WaitForExecutionAsync(JobTimeout);

        executedAt.Should().BeOnOrAfter(enqueuedAt,
            because: "job execution must happen after enqueue");
    }

    // -------------------------------------------------------------------------
    // TC-JOB-INT-002: ScheduleAsync delays execution by at least the specified delay
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-JOB-INT-002")]
    [Trait("Category", "SlowIntegration")]
    public async Task Schedule_WithDelay_JobExecutesAfterDelay()
    {
        TestScheduledJob.Reset();

        var delay = TimeSpan.FromSeconds(2);
        var scheduledAt = DateTimeOffset.UtcNow;

        // Schedule with explicit delay
        fixture.JobClient.Schedule<TestScheduledJob>(
            j => j.ExecuteAsync(),
            delay);

        // Wait up to JobTimeout for execution — must be at least `delay` after schedule
        var executedAt = await TestScheduledJob.WaitForExecutionAsync(JobTimeout);

        executedAt.Should().BeOnOrAfter(
            scheduledAt.Add(delay).AddSeconds(-0.5),  // 0.5s tolerance for polling jitter
            because: $"scheduled job should not execute before the {delay.TotalSeconds}s delay");
    }

    // -------------------------------------------------------------------------
    // TC-JOB-INT-003: RecurringJob fires on schedule (every-minute cron triggers promptly)
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-JOB-INT-003")]
    [Trait("Category", "SlowIntegration")]
    public async Task AddOrUpdateRecurring_WithMinuteCron_JobExecutesWithinWindow()
    {
        TestRecurringJob.Reset();

        var jobId = $"test-recurring-{Guid.NewGuid():N}";

        // Register recurring job with every-minute cron
        // Hangfire InMemory with SchedulePollingInterval=100ms will trigger it promptly
        fixture.RecurringJobManager.AddOrUpdate<TestRecurringJob>(
            jobId,
            j => j.ExecuteAsync(),
            Cron.Minutely());

        // Trigger the recurring job immediately (avoids waiting for the next minute boundary)
        fixture.RecurringJobManager.Trigger(jobId);

        // Wait for the worker to execute the triggered instance
        var executedAt = await TestRecurringJob.WaitForExecutionAsync(JobTimeout);

        executedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15),
            because: "triggered recurring job should execute within the test window");

        // Clean up — remove recurring job so it does not fire again during later tests
        fixture.RecurringJobManager.RemoveIfExists(jobId);
    }
}
