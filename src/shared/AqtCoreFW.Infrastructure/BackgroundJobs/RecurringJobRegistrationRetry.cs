using Hangfire.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.BackgroundJobs;

/// <summary>
/// Boot-time resilience wrapper for recurring-job registration.
///
/// Registering recurring jobs at startup drives Hangfire's static <c>RecurringJob</c> facade,
/// which acquires a SQL distributed lock on <c>JobStorage.Current</c>. On a contended or slow-SQL
/// boot that lock acquisition can time out transiently and throw — with no handling, that hard-fails
/// the entire API host boot (a K8s restart storm on sustained contention).
///
/// This helper retries the (idempotent — every call is a Hangfire <c>AddOrUpdate</c> upsert)
/// registration on transient lock-timeout failures, then <b>fails soft</b>: after the attempts are
/// exhausted it logs Critical and lets boot continue. Fail-soft only skips <b>re-registration</b> of
/// the recurring schedules, NOT their execution: the schedule definitions live in Hangfire's storage
/// once persisted by any prior successful boot, so the Hangfire server keeps firing them (including
/// compliance jobs — audit-log purge / RTBF SLA / alert eval) on schedule regardless of whether this
/// boot re-registered them. The only window with a truly-unscheduled job is a first-ever deploy whose
/// every boot on every replica exhausts all retries — extreme and self-healing on the next boot, and a
/// box that genuinely cannot reach SQL already reports Degraded via the DB health check.
///
/// Non-transient exceptions (e.g. a wiring bug surfacing as <c>NullReferenceException</c>) are NOT
/// swallowed — they rethrow immediately so real defects still surface at boot.
///
/// KISS: a bounded loop, no Polly (the host has no Polly reference). See
/// <c>plans/260721-0900-p1-stable-migration-predeploy-dryrun/phase-01-hangfire-boot-lock-retry.md</c>.
/// </summary>
public static class RecurringJobRegistrationRetry
{
    /// <summary>
    /// SQL Server error numbers that indicate a transient lock/timeout during lock acquisition:
    /// 1222 = "Lock request time out period exceeded"; -2 = client-side command timeout.
    /// </summary>
    private static readonly int[] TransientSqlErrorNumbers = { 1222, -2 };

    /// <summary>
    /// True when <paramref name="ex"/> (or any of its inner exceptions) is a Hangfire distributed
    /// lock-timeout or a lock/timeout <see cref="SqlException"/>. Pure — no I/O; this is the
    /// unit-tested classification seam. Match by type + SQL error number, never by message text,
    /// to stay robust across locales and driver versions.
    /// </summary>
    public static bool IsTransientLockFailure(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is DistributedLockTimeoutException)
            {
                return true;
            }

            if (current is SqlException sqlEx && IsTransientSqlException(sqlEx))
            {
                return true;
            }

            // AggregateException can carry the real cause across InnerExceptions (not just InnerException).
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (IsTransientLockFailure(inner))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsTransientSqlException(SqlException sqlEx)
    {
        // A SqlException aggregates one or more errors; treat it as transient if any error number matches.
        foreach (SqlError error in sqlEx.Errors)
        {
            if (Array.IndexOf(TransientSqlErrorNumbers, error.Number) >= 0)
            {
                return true;
            }
        }

        return Array.IndexOf(TransientSqlErrorNumbers, sqlEx.Number) >= 0;
    }

    /// <summary>
    /// Runs <paramref name="register"/> with bounded retry on transient lock-timeout failures.
    /// On the final attempt still failing, logs Critical and returns (fail-soft). A non-transient
    /// exception rethrows immediately.
    /// </summary>
    /// <param name="register">The recurring-job registration action (idempotent upserts).</param>
    /// <param name="logger">Boot logger for the fail-soft Critical message.</param>
    /// <param name="maxAttempts">Total attempts including the first (default 3).</param>
    /// <param name="backoff">Delay before attempt <c>n</c> (1-based); default linear 2s/4s/6s.</param>
    /// <param name="sleep">Injectable sleeper (default <see cref="Thread.Sleep(TimeSpan)"/>); the
    /// unit test passes a no-op to assert attempt count without wall-clock cost.</param>
    public static void RunWithRetry(
        Action register,
        ILogger logger,
        int maxAttempts = 3,
        Func<int, TimeSpan>? backoff = null,
        Action<TimeSpan>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(logger);
        backoff ??= n => TimeSpan.FromSeconds(2 * n);
        sleep ??= Thread.Sleep;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                register();
                return;
            }
            catch (Exception ex) when (IsTransientLockFailure(ex))
            {
                if (attempt >= maxAttempts)
                {
                    logger.LogCritical(
                        ex,
                        "Recurring-job registration failed after {Attempts} attempts on a transient Hangfire " +
                        "lock timeout; jobs are NOT registered this boot. They will re-register on the next boot " +
                        "or from another replica. Boot continues (fail-soft).",
                        maxAttempts);
                    return;
                }

                var delay = backoff(attempt);
                logger.LogWarning(
                    ex,
                    "Recurring-job registration hit a transient Hangfire lock timeout on attempt {Attempt}/{MaxAttempts}; " +
                    "retrying in {DelaySeconds}s.",
                    attempt,
                    maxAttempts,
                    delay.TotalSeconds);
                sleep(delay);
            }
        }
    }
}
