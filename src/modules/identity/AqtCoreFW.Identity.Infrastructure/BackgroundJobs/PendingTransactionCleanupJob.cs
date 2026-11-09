using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Background hosted service that marks overdue <see cref="SigningPendingTransaction"/> rows as Expired.
///
/// <para>Runs every 30 minutes via <see cref="PeriodicTimer"/>.
/// Targets rows where <c>Status = Initiated AND ExpiresAt &lt; UtcNow - CleanupGraceMinutes</c>.
/// Grace period prevents the cleanup job racing an in-flight callback that arrives just after ExpiresAt.</para>
///
/// <para>NOTE: This service is stripped in integration tests (not an <c>IIntegrationTestSeeder</c>).
/// Cleanup correctness is verified via direct DB manipulation in unit tests.</para>
/// </summary>
public sealed class PendingTransactionCleanupJob(
    IServiceProvider serviceProvider,
    IOptionsMonitor<ViettelMysignOptions> opts,
    ILogger<PendingTransactionCleanupJob> logger)
    : IHostedService, IAsyncDisposable
{
    private const int RunIntervalMinutes = 30;

    private PeriodicTimer? _timer;
    private Task? _runTask;
    private CancellationTokenSource? _cts;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new PeriodicTimer(TimeSpan.FromMinutes(RunIntervalMinutes));
        _runTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_timer is null) return;

        _timer.Dispose();
        _timer = null;

        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_runTask is not null)
        {
            try
            {
                await _runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected on shutdown */ }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        _cts?.Dispose();
        if (_runTask is not null)
            await _runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    // ── Private run loop ──────────────────────────────────────────────────────

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
                // Non-fatal — log and continue; next tick will retry.
                logger.LogError(ex,
                    "PendingTransactionCleanupJob: unexpected error during cleanup sweep");
            }
        }
    }

    /// <summary>
    /// Performs one cleanup sweep: finds all Initiated transactions past their expiry grace period and marks
    /// them Expired. Runs per-tenant with SESSION_CONTEXT pinned (via <see cref="TenantScopedSweep"/>) so the
    /// sweep stays correct under a least-privilege DB login — an all-tenant query without tenant context would
    /// match zero RLS-protected rows and silently skip expiry there.
    /// </summary>
    internal async Task RunOnceAsync(CancellationToken ct = default)
    {
        var graceMinutes = opts.CurrentValue.PendingTxCleanupGraceMinutes;
        var cutoff       = DateTime.UtcNow.AddMinutes(-graceMinutes);

        var total = await TenantScopedSweep.RunAsync(
            serviceProvider,
            async (db, tenantId, token) =>
            {
                // Explicit TenantId predicate (belt-and-braces): under a db_owner connection the RLS filter is
                // bypassed, so this keeps each row processed by exactly its owning tenant's iteration.
                var expired = await db.SigningPendingTransactions
                    .Where(t => t.TenantId == tenantId
                                && t.Status == PendingTransactionStatus.Initiated
                                && t.ExpiresAt < cutoff)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                if (expired.Count == 0)
                    return 0;

                foreach (var tx in expired)
                    tx.MarkExpired();

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return expired.Count;
            },
            logger,
            ct).ConfigureAwait(false);

        if (total == 0)
        {
            logger.LogDebug("PendingTransactionCleanupJob: no expired transactions found");
            return;
        }

        logger.LogInformation(
            "PendingTransactionCleanupJob: marked {Count} pending transaction(s) as Expired " +
            "(cutoff={Cutoff:u}, graceMinutes={Grace})",
            total, cutoff, graceMinutes);
    }
}
