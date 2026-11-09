using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// Hourly sweep that hard-deletes <see cref="ProcessedMessage"/> ledger rows older than the retention
/// window so <c>messaging.processed_messages</c> stays bounded. The ledger only needs an entry for as
/// long as a duplicate delivery of that message could still arrive; once the outbox row is processed or
/// dead-lettered and the broker's redelivery window has passed, the key is dead weight. The window
/// (<see cref="RetentionDays"/>) is set generously beyond any outbox retry / broker redelivery horizon.
///
/// <para>Set-based <see cref="EntityFrameworkQueryableExtensions.ExecuteDeleteAsync"/> keyed by
/// <c>IX_ProcessedMessages_ProcessedAtUtc</c> — no row loading, since bounded growth is the whole point.
/// The table carries no RLS (a cross-cutting delivery concern, not tenant data), so this is a single
/// all-rows statement, not a per-tenant sweep. NOTE: stripped in integration tests (not an
/// <c>IIntegrationTestSeeder</c>); correctness is verified by driving <see cref="RunOnceAsync"/> directly.</para>
/// </summary>
public sealed class ProcessedMessageCleanupJob(
    IServiceProvider serviceProvider,
    ILogger<ProcessedMessageCleanupJob> logger)
    : IHostedService, IAsyncDisposable
{
    private const int RunIntervalMinutes = 60;

    /// <summary>Ledger rows older than this are eligible for deletion — comfortably beyond any
    /// outbox retry / broker redelivery window in which a duplicate could still appear.</summary>
    private const int RetentionDays = 30;

    private PeriodicTimer? _timer;
    private Task? _runTask;
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(RunIntervalMinutes));
        _runTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_timer is null) return;

        _timer.Dispose();
        _timer = null;

        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        if (_runTask is not null)
        {
            try { await _runTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected on shutdown */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        _cts?.Dispose();
        if (_runTask is not null)
            await _runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        if (_timer is null) return;

        while (await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await RunOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Non-fatal — log and continue; next tick retries.
                logger.LogError(ex, "ProcessedMessageCleanupJob: unexpected error during cleanup sweep");
            }
        }
    }

    /// <summary>
    /// One sweep: hard-deletes every ledger row older than the retention window in a single set-based
    /// statement. Returns the number of rows removed. Public so the retention predicate — which is
    /// load-bearing (a wrong comparison would delete live keys and silently re-enable double-effects) —
    /// can be driven directly by an integration test without widening the shared assembly's internals.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);

        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();

        var deleted = await db.ProcessedMessages
            .Where(m => m.ProcessedAtUtc < cutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (deleted == 0)
        {
            logger.LogDebug("ProcessedMessageCleanupJob: no expired idempotency rows found");
            return 0;
        }

        logger.LogInformation(
            "ProcessedMessageCleanupJob: deleted {Count} expired idempotency row(s) (cutoff={Cutoff:u})",
            deleted, cutoff);
        return deleted;
    }
}
