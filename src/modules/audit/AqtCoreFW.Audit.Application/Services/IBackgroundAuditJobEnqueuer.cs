namespace AqtCoreFW.Audit.Application.Services;

/// <summary>
/// Abstracts Hangfire enqueueing so Application layer has no Hangfire dependency.
/// Infrastructure provides the implementation.
/// </summary>
public interface IBackgroundAuditJobEnqueuer
{
    /// <summary>
    /// Enqueue async HMAC chain signing for all unsigned entries belonging to the given tenant.
    /// NULL tenantId enqueues the system-level (null-tenant) chain.
    /// One job per tenant: the batch signer holds a per-tenant advisory lock (sp_getapplock)
    /// so concurrent calls for the same tenant serialize without forking the chain.
    /// </summary>
    void EnqueueHmacChain(Guid? tenantId);

    /// <summary>
    /// Enqueue the full-system HMAC chain backfill job on the audit lane.
    /// Returns the Hangfire job id so the HTTP endpoint can return 202 Accepted with the id.
    /// Actor identity is captured at enqueue time (from the HTTP-bound ICurrentUser) and passed
    /// as job arguments so the background job can write the audit trail entry with the correct
    /// triggering actor — Hangfire jobs run without an HTTP context so ICurrentUser is empty there.
    /// The caller IP and correlation id are captured at the same point (HTTP-bound) and recorded
    /// on the backfill audit entry as tamper-evidence: a privileged signature rewrite leaves a
    /// forensic trail of who/where/when, not just the row count.
    /// </summary>
    string EnqueueBackfillChain(
        Guid triggeredByUserId,
        Guid? triggeredByTenantId,
        string triggeredByUserName,
        string? triggeredByIp,
        string? correlationId);

    /// <summary>
    /// Enqueue a durable retry for an audit entry whose synchronous write failed (cell #7 L1
    /// ingestion-loss). The intent carries the fully-frozen row state captured at request time, so
    /// the background job rebuilds and re-inserts the row (idempotent on MessageId) WITHOUT any HTTP
    /// context or request principal. Runs on the dedicated audit lane; capped automatic retries end
    /// in Hangfire's Failed state (the dead-letter) so a persistently un-writable audit event stays
    /// visible and re-queueable rather than silently gone.
    /// </summary>
    void EnqueueIngestionRetry(AuditIngestionIntent intent);
}
