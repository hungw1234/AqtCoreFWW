using AqtCoreFW.Audit.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job — the safety net for per-tenant HMAC chain signing.
///
/// Audit-chain signing is normally driven by append: every audit write enqueues an incremental sign
/// for that entry's tenant. That leaves a gap for a chain that stops receiving new entries — a
/// low-traffic chain (e.g. the system/NULL chain) never re-fires the append-time enqueue, so rows
/// left at SequenceId=0 would stay unsigned indefinitely and trip the unsigned-backlog health check.
///
/// This job runs on a short recurring schedule, finds every tenant chain with an unsigned backlog and
/// enqueues an incremental sign for it. It does NOT sign inline and does NOT write any audit entry —
/// it only enqueues, so it cannot fork the chain or create a self-perpetuating backfill loop. The
/// per-tenant applock in the signer serializes against append-time enqueues, so a redundant enqueue
/// is an idempotent no-op once the rows are signed.
///
/// Scheduled every 5 minutes to match the unsigned-backlog health check's age threshold (two sweep
/// intervals = the 10-minute oldest-unsigned tolerance).
/// </summary>
public sealed class AuditChainSweepJob
{
    private readonly HmacChainService _chainService;
    private readonly ILogger<AuditChainSweepJob> _logger;

    public AuditChainSweepJob(HmacChainService chainService, ILogger<AuditChainSweepJob> logger)
    {
        _chainService = chainService;
        _logger = logger;
    }

    /// <summary>Hangfire entry point — enqueue an incremental sign for every backlogged tenant chain.</summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var enqueued = await _chainService.EnqueueSignForBackloggedChainsAsync(cancellationToken);

        if (enqueued == 0)
            _logger.LogDebug("AuditChainSweepJob: no backlogged chains — nothing to enqueue.");
        else
            _logger.LogInformation(
                "AuditChainSweepJob: enqueued sign for {Count} backlogged tenant chain(s).", enqueued);
    }
}
