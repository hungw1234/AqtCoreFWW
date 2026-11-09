using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Events;

namespace AqtCoreFW.Audit.Infrastructure.Services;

/// <summary>
/// The single audit-row persist path, shared VERBATIM by the synchronous request path
/// (<see cref="AuditService"/>) and the durable retry path (<see cref="Jobs.AuditIngestionRetryJob"/>)
/// so both land byte-identical outbox + HMAC-chain semantics. Extracted from AuditService so the
/// retry job can reuse it WITHOUT AuditService's request-scoped ambient reads
/// (<c>IHttpContextAccessor</c> / <c>ICurrentUser</c>) — a background job has neither.
/// </summary>
public sealed class AuditEntryPersister(
    IAuditLogRepository repository,
    ICrossModuleUnitOfWork uow,
    IOutboxWriter outboxWriter,
    IBackgroundAuditJobEnqueuer jobEnqueuer,
    IGuidGenerator guidGenerator)
{
    /// <summary>
    /// Atomically write the audit row + its integration event, then enqueue per-tenant HMAC signing.
    ///
    /// The write span is intentionally NOT cancellable (<see cref="CancellationToken.None"/>): it runs
    /// AFTER the business command has already committed its own unit of work, so a request-abort must
    /// never drop the audit row — that cancellation-swallow is one of the L1 ingestion-loss paths this
    /// fix closes. The cost of ignoring the request token is bounded by the SQL command timeout.
    /// </summary>
    public async Task PersistAsync(AuditLogEntry entry)
    {
        // Cross-module integration event — additive prep for future microservice extraction (consumer
        // noop today). NO dataSnapshot on the wire (PII rules per AuditPiiAnonymizer). MessageId is the
        // event's OWN id (distinct from entry.MessageId, the audit row's AK).
        var integrationEvent = new AuditLogAppendedIntegrationEvent(
            entry.MessageId,
            entry.TenantId,
            entry.UserId,
            entry.Action,
            entry.ModuleName,
            entry.ResourceType,
            entry.ResourceId,
            guidGenerator.NewIdMonotonic(),
            entry.OccurredAt);

        // Atomic write span: the audit row + its outbox enqueue commit or roll back together — an audit
        // row can never exist without its integration event enqueued, and no event is ever emitted for a
        // row that never persisted (at-least-once delivery via the outbox dispatcher).
        //
        // Nesting-aware ownership (mirrors SubmitFormCommandHandler): the audit logger runs standalone for
        // every caller today (AuditBehavior fires AFTER the inner handler committed its own UoW; direct
        // callers never touch the UoW) — so ownsUow is currently always true. The check still guards
        // correctness if a future caller ever logs from WITHIN its own still-open UoW span: only the owner
        // commits/rolls back, so the audit row and the outer write land in the same local SQL transaction.
        // Pin the write span's SESSION_CONTEXT to the ROW's tenant, not the ambient one. On the sync
        // request path entry.TenantId == the ambient tenant, so this is behavior-preserving; on the
        // durable-retry path the ambient context is the null BackgroundJobTenantContext, so WITHOUT this
        // the RLS BLOCK predicate rejects a named-tenant INSERT under the prod least-privilege
        // (non-db_owner) login — the row would fail to persist and the recovery would silently no-op.
        // A null-tenant (system) row needs no override: the BLOCK predicate always permits TenantId IS NULL.
        var ownsUow = !uow.IsActive;
        if (ownsUow)
        {
            if (entry.TenantId is Guid rowTenantId)
                await uow.BeginAsync(rowTenantId, CancellationToken.None);
            else
                await uow.BeginAsync(CancellationToken.None);
        }
        try
        {
            await repository.AddAsync(entry, CancellationToken.None);
            await outboxWriter.EnqueueAsync(integrationEvent, "audit", CancellationToken.None);
            if (ownsUow) await uow.CommitAsync(CancellationToken.None);
        }
        catch
        {
            if (ownsUow) await uow.RollbackAsync(CancellationToken.None);
            throw;
        }

        // HMAC chain enqueue MUST run only after the row is durably committed — HmacChainService reads
        // AuditLogEntries on its own connection and would find nothing to sign if it raced ahead of the
        // commit above. Passing TenantId (not MessageId) lets the batch signer use sp_getapplock keyed
        // per-tenant so concurrent calls for the same tenant serialize without forking the chain.
        //
        // When nested (ownsUow == false — dormant today) the outer caller commits later; enqueuing here
        // would race that not-yet-committed row, so it is skipped. AuditChainSweepJob (5-minute safety net)
        // picks up the entry once its owning transaction eventually commits.
        if (ownsUow)
            jobEnqueuer.EnqueueHmacChain(entry.TenantId);
    }
}
