using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Application.Telemetry;
using AqtCoreFW.Audit.Infrastructure.Services;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Jobs;

/// <summary>
/// Durable recovery for an audit write that failed its synchronous path (cell #7 L1 ingestion-loss).
/// <see cref="AuditService"/> enqueues this job with a fully-frozen <see cref="AuditIngestionIntent"/>
/// when the sync persist throws; the job rebuilds and re-inserts the row on the dedicated audit lane.
///
/// Idempotency is effect-is-the-marker: the audit row's alternate key (MessageId, a globally-unique v7
/// GUID) is the dedup marker. The job simply attempts the insert; a unique-key violation means THIS
/// exact event's row already exists (a Hangfire retry replay, a concurrent retry racing, or the F-H
/// window where the sync path committed the row but a LATER step — HMAC enqueue — threw) → that is
/// idempotent success, not a failure: the missed chain sign is (re)driven and the job returns. Running
/// the job any number of times yields exactly one row. This is the correct at-least-once pattern for a
/// retry-durable consumer with an idempotent effect (NOT a claim-first ledger, which would silently lose
/// on post-claim failure — see repo lesson feedback-claim-first-dedup-only-for-at-most-once-consumers).
///
/// Insert-then-catch (rather than read-then-insert) is deliberate: under the prod least-privilege
/// (non-db_owner) login the RLS FILTER predicate hides a named-tenant row from a dedup read that runs
/// without the tenant's SESSION_CONTEXT, so a read-first check would be blind and race-prone. The unique
/// constraint is the authoritative guard on every login.
///
/// The job NEVER reads <c>IHttpContextAccessor</c> or <c>ICurrentUser</c> — all attribution comes from
/// the frozen intent. A background job has no request principal, so re-deriving tenant/user would
/// misattribute the row to the null/system chain. The RLS write context is pinned from the intent's
/// tenant inside <see cref="AuditEntryPersister.PersistAsync"/>.
///
/// <c>[AutomaticRetry(Attempts = 10)]</c>: transient audit-DB stress recovers within the retry window;
/// a persistently un-writable intent (poison) exhausts the cap and lands in Hangfire's Failed state —
/// the dead-letter — where it stays visible and re-queueable via the dashboard rather than silently
/// gone. No custom dead-letter table.
/// </summary>
[AutomaticRetry(Attempts = 10)]
public sealed class AuditIngestionRetryJob(
    AuditEntryPersister persister,
    IBackgroundAuditJobEnqueuer jobEnqueuer,
    ILogger<AuditIngestionRetryJob> logger)
{
    // SQL Server error numbers for a unique-constraint / duplicate-key violation.
    private const int UniqueConstraintViolation = 2627;
    private const int DuplicateKeyInUniqueIndex = 2601;

    /// <summary>Hangfire entry point — rebuild and durably re-insert the failed audit row (idempotent).</summary>
    public async Task ExecuteAsync(AuditIngestionIntent intent, CancellationToken cancellationToken = default)
    {
        var moduleTag = new KeyValuePair<string, object?>("module", intent.ModuleName);
        try
        {
            await persister.PersistAsync(intent.ToEntry());
            AuditMetrics.IngestionRecovered.Add(1, moduleTag);
            logger.LogInformation(
                "AuditIngestionRetryJob: recovered audit entry {MessageId} for {Module}/{Action}.",
                intent.MessageId, intent.ModuleName, intent.Action);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // The row already exists (F-H window or a racing/replayed retry). Not a failure — the audit
            // is durable; just make sure its chain sign is driven (the sync path may have died before it).
            jobEnqueuer.EnqueueHmacChain(intent.TenantId);
            AuditMetrics.IngestionRecovered.Add(1, moduleTag);
            logger.LogInformation(
                "AuditIngestionRetryJob: entry {MessageId} already present — re-enqueued chain sign only.",
                intent.MessageId);
        }
    }

    /// <summary>True when the exception (or an inner one) is a SQL unique-key / duplicate-key violation.</summary>
    private static bool IsDuplicateKey(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql &&
                sql.Number is UniqueConstraintViolation or DuplicateKeyInUniqueIndex)
                return true;
        }
        return false;
    }
}
