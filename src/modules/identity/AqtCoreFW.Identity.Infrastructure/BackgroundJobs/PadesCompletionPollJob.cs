using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Background hosted service that drives the PAdES async-poll completion: every minute it scans Initiated
/// transactions carrying PAdES resume state, polls Mysign for each, and on a terminal signature splices the CMS
/// into the staged placeholder and stores the signed PDF (via <see cref="IPadesCompletionService"/>).
///
/// <para>Mirrors <see cref="PendingTransactionCleanupJob"/>: a <see cref="PeriodicTimer"/> loop with a fresh scope
/// per sweep. Each transaction is processed and saved independently so one provider error / bad row cannot block
/// the others, and a replayed sweep is safe (the completion service is idempotent on terminal rows).</para>
///
/// <para>NOT registered in the Testing environment — WebAppFixture strips non-seeder hosted services; completion
/// correctness is covered by <c>PadesCompletionServiceTests</c>.</para>
///
/// <para><b>HA multi-instance safe.</b> A <see cref="PeriodicTimer"/> never overlaps its own ticks, so a single
/// host self-serializes; across multiple hosts two sweeps can load the same row, but the completion path is
/// idempotent by construction. The signed-PDF key is derived deterministically from the transaction identity
/// (<see cref="PadesCompletionService"/>), so two replicas that both reach terminal success write byte-identical
/// content to the SAME key → the object store's overwrite-by-key PUT collapses them to one blob (no stranded
/// orphan). The RowVersion token then elects exactly one Initiated→Completed transition (the loser's save throws a
/// concurrency exception, swallowed by the per-row catch below). This removes the single-host requirement the
/// pre-Phase-8 design carried (the same deterministic-key idempotency also closes the DocFlow orphan class —
/// see <c>SIGN-DOCFLOW-DETERMINISTIC-BLOB-KEY</c>).</para>
/// </summary>
public sealed class PadesCompletionPollJob(
    IServiceProvider serviceProvider,
    ILogger<PadesCompletionPollJob> logger) : IHostedService, IAsyncDisposable
{
    private const int RunIntervalSeconds = 60;

    private PeriodicTimer? _timer;
    private Task? _runTask;
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(RunIntervalSeconds));
        _runTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_timer is null) return;
        _timer.Dispose();
        _timer = null;
        if (_cts is not null) await _cts.CancelAsync().ConfigureAwait(false);
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
                logger.LogError(ex, "PadesCompletionPollJob: unexpected error during completion sweep");
            }
        }
    }

    /// <summary>
    /// One sweep: load every Initiated PAdES transaction and attempt completion. Each row is saved independently
    /// so a single provider/storage error is logged and isolated — the rest of the sweep still progresses.
    /// </summary>
    internal async Task RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var completion = scope.ServiceProvider.GetRequiredService<IPadesCompletionService>();

        var due = await db.SigningPendingTransactions
            .Where(t => t.Status == PendingTransactionStatus.Initiated && t.PadesPlaceholderStorageKey != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (due.Count == 0)
        {
            logger.LogDebug("PadesCompletionPollJob: no pending PAdES transactions to poll");
            return;
        }

        foreach (var txn in due)
        {
            try
            {
                await completion.CompleteAsync(txn, ct).ConfigureAwait(false);
                // Save per-row so a downstream failure does not roll back an already-finalized sibling.
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Infra fault (Mysign down, storage, concurrency) — leave the row Initiated for the next tick.
                logger.LogWarning(ex,
                    "PadesCompletionPollJob: completion attempt failed for txn {TxId}; will retry next tick",
                    txn.TransactionId);
            }
        }
    }
}
