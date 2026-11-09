using AqtCoreFW.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// Pure unit tests for <see cref="RecurringJobRegistrationRetry"/> — the boot-lock retry seam that
/// keeps a transient Hangfire lock timeout from hard-failing API host boot. No DB / container: the
/// classification and retry-loop are pure functions with an injectable sleeper.
/// </summary>
public sealed class RecurringJobRegistrationRetryTests
{
    // === IsTransientLockFailure classification ===

    [Fact]
    public void IsTransientLockFailure_true_for_distributed_lock_timeout()
    {
        var ex = new DistributedLockTimeoutException("recurring-jobs:lock");

        RecurringJobRegistrationRetry.IsTransientLockFailure(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTransientLockFailure_false_for_a_non_transient_exception()
    {
        RecurringJobRegistrationRetry.IsTransientLockFailure(new InvalidOperationException("bad wiring"))
            .Should().BeFalse();
    }

    [Fact]
    public void IsTransientLockFailure_false_for_null()
    {
        RecurringJobRegistrationRetry.IsTransientLockFailure(null).Should().BeFalse();
    }

    [Fact]
    public void IsTransientLockFailure_true_when_transient_is_nested_as_inner()
    {
        var ex = new InvalidOperationException(
            "registration failed",
            new DistributedLockTimeoutException("recurring-jobs:lock"));

        RecurringJobRegistrationRetry.IsTransientLockFailure(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTransientLockFailure_true_when_transient_is_inside_an_aggregate()
    {
        var ex = new AggregateException(
            new InvalidOperationException("unrelated"),
            new DistributedLockTimeoutException("recurring-jobs:lock"));

        RecurringJobRegistrationRetry.IsTransientLockFailure(ex).Should().BeTrue();
    }

    // === RunWithRetry behaviour (no-op sleeper → no wall-clock cost) ===

    [Fact]
    public void RunWithRetry_retries_up_to_maxAttempts_then_fails_soft()
    {
        var attempts = 0;
        var logger = new CountingLogger();

        RecurringJobRegistrationRetry.RunWithRetry(
            register: () =>
            {
                attempts++;
                throw new DistributedLockTimeoutException("recurring-jobs:lock");
            },
            logger: logger,
            maxAttempts: 3,
            sleep: _ => { /* no-op: skip the backoff delay in tests */ });

        // Fail-soft: no throw escapes; the action was tried exactly maxAttempts times.
        attempts.Should().Be(3);
        logger.CriticalCount.Should().Be(1);
    }

    [Fact]
    public void RunWithRetry_succeeds_on_second_attempt_without_logging_critical()
    {
        var attempts = 0;
        var logger = new CountingLogger();

        RecurringJobRegistrationRetry.RunWithRetry(
            register: () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new DistributedLockTimeoutException("recurring-jobs:lock");
                }
            },
            logger: logger,
            maxAttempts: 3,
            sleep: _ => { });

        attempts.Should().Be(2);
        logger.CriticalCount.Should().Be(0);
    }

    [Fact]
    public void RunWithRetry_rethrows_a_non_transient_exception_immediately()
    {
        var attempts = 0;

        var act = () => RecurringJobRegistrationRetry.RunWithRetry(
            register: () =>
            {
                attempts++;
                throw new InvalidOperationException("real wiring bug");
            },
            logger: NullLogger.Instance,
            maxAttempts: 3,
            sleep: _ => { });

        act.Should().Throw<InvalidOperationException>().WithMessage("real wiring bug");
        attempts.Should().Be(1); // no retry on a non-transient failure
    }

    /// <summary>Minimal <see cref="ILogger"/> spy counting only Critical-level writes.</summary>
    private sealed class CountingLogger : ILogger
    {
        public int CriticalCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Critical)
            {
                CriticalCount++;
            }
        }
    }
}
