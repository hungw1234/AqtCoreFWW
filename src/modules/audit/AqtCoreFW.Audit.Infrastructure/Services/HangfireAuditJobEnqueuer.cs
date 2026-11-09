using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Infrastructure.Jobs;
using AqtCoreFW.SharedKernel.Application;
using Hangfire;

namespace AqtCoreFW.Audit.Infrastructure.Services;

/// <summary>
/// Hangfire-backed implementation of IBackgroundAuditJobEnqueuer.
/// Decouples Application layer from Hangfire — Application only sees the interface.
/// </summary>
public sealed class HangfireAuditJobEnqueuer : IBackgroundAuditJobEnqueuer
{
    /// <summary>
    /// Enqueues a per-tenant HMAC chain signing job on the dedicated audit lane.
    /// The job signs ALL unsigned entries for the tenant in one batch, holding a per-tenant
    /// advisory lock (sp_getapplock) so concurrent enqueues for the same tenant serialize
    /// without forking the chain. NULL tenantId targets the system-level chain.
    /// </summary>
    public void EnqueueHmacChain(Guid? tenantId) =>
        BackgroundJob.Enqueue<HmacChainService>(BackgroundJobQueues.Audit, svc =>
            svc.SignTenantChainAsync(tenantId, CancellationToken.None));

    /// <summary>
    /// Enqueues the full-system HMAC chain backfill on the dedicated audit lane.
    /// Actor identity is passed as job arguments because Hangfire jobs run without an HTTP context
    /// (ICurrentUser is empty there) — the job writes the audit trail entry using the captured actor.
    /// Returns the Hangfire job id so the HTTP endpoint can return 202 Accepted.
    /// </summary>
    public string EnqueueBackfillChain(
        Guid triggeredByUserId,
        Guid? triggeredByTenantId,
        string triggeredByUserName,
        string? triggeredByIp,
        string? correlationId) =>
        BackgroundJob.Enqueue<HmacChainService>(BackgroundJobQueues.Audit, svc =>
            svc.RunBackfillJobAsync(
                triggeredByUserId, triggeredByTenantId, triggeredByUserName,
                triggeredByIp, correlationId, CancellationToken.None));

    /// <summary>
    /// Enqueues the durable audit-ingestion retry on the dedicated audit lane. The intent carries the
    /// frozen row state, so <see cref="AuditIngestionRetryJob"/> rebuilds the row without any request
    /// context. Fire-and-forget: the enqueue is what makes the failed write recoverable.
    /// </summary>
    public void EnqueueIngestionRetry(AuditIngestionIntent intent) =>
        BackgroundJob.Enqueue<AuditIngestionRetryJob>(BackgroundJobQueues.Audit, job =>
            job.ExecuteAsync(intent, CancellationToken.None));
}
