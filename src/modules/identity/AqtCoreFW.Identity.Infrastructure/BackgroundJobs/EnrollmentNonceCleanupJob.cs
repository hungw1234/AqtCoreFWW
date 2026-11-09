using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Background hosted service that hard-deletes spent <see cref="SignerEnrollmentNonce"/> rows so the table
/// does not grow without bound. Each issue-challenge writes one short-lived (TTL 300s) single-use nonce;
/// once consumed or expired the row is dead weight — it can never authorise an enrollment again.
///
/// <para>Runs hourly via <see cref="PeriodicTimer"/>. Deletes rows where
/// <c>ConsumedAt IS NOT NULL OR ExpiresAt &lt; UtcNow</c> with a single set-based <c>ExecuteDeleteAsync</c>
/// (no row-loading — bounded-growth is the whole point), indexed by <c>IX_SignerEnrollmentNonce_ExpiresAt</c>.</para>
///
/// <para>NO grace period (unlike <see cref="PendingTransactionCleanupJob"/>): there is no valid in-flight
/// operation a grace would protect. A nonce past <c>ExpiresAt</c> is already rejected on read
/// (<see cref="SignerEnrollmentNonce.IsExpired"/>), and a consumed nonce is terminal (single-use) — so a
/// just-barely-expired submit fails regardless of whether the row still exists. Prompt deletion is correct.</para>
///
/// <para>Tenant isolation is RLS-backed and the target table (<c>SignerEnrollmentNonces</c>) carries an RLS
/// policy, so the sweep runs PER-TENANT with SESSION_CONTEXT pinned (via <see cref="TenantScopedSweep"/>)
/// rather than as a single all-tenant statement — the latter would match zero rows under a least-privilege
/// login. Tenants are enumerated from the non-RLS <c>Tenants</c> registry (no db_owner needed).</para>
///
/// <para>NOTE: stripped in integration tests (not an <c>IIntegrationTestSeeder</c>); correctness is verified
/// by driving <see cref="RunOnceAsync"/> directly against real SQL.</para>
/// </summary>
public sealed class EnrollmentNonceCleanupJob(
    IServiceProvider serviceProvider,
    ILogger<EnrollmentNonceCleanupJob> logger)
    : IHostedService, IAsyncDisposable
{
    private const int RunIntervalMinutes = 60;

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
                    "EnrollmentNonceCleanupJob: unexpected error during cleanup sweep");
            }
        }
    }

    /// <summary>
    /// Performs one cleanup sweep: hard-deletes every consumed or expired enrollment nonce in a single
    /// set-based statement. Returns the number of rows removed (for the caller/test to assert on).
    /// </summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Per-tenant sweep with SESSION_CONTEXT pinned so the RLS-protected delete matches rows under a
        // least-privilege login. Explicit TenantId predicate keeps each row deleted by exactly its owning
        // tenant's iteration even under a db_owner connection (where the RLS filter is bypassed).
        // Strict `<` (vs the read-path's `IsExpired` which is `utcNow >= ExpiresAt`): at the exact-equality
        // tick the row is already read-rejected but retained here — harmless, it is swept on the next pass.
        var deleted = await TenantScopedSweep.RunAsync(
            serviceProvider,
            (db, tenantId, token) =>
                db.SignerEnrollmentNonces
                    .Where(n => n.TenantId == tenantId
                                && (n.ConsumedAt != null || n.ExpiresAt < now))
                    .ExecuteDeleteAsync(token),
            logger,
            ct).ConfigureAwait(false);

        if (deleted == 0)
        {
            logger.LogDebug("EnrollmentNonceCleanupJob: no spent enrollment nonces found");
            return 0;
        }

        logger.LogInformation(
            "EnrollmentNonceCleanupJob: deleted {Count} spent enrollment nonce(s) (cutoff={Cutoff:u})",
            deleted, now);
        return deleted;
    }
}
