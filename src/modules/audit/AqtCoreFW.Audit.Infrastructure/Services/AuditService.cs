using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Application.Telemetry;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Services;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of IAuditService + IAuditLogger.
/// IAuditLogger is used by MediatR AuditBehavior for automatic command logging.
/// </summary>
public sealed class AuditService(
    AuditDbContext db,
    IBackgroundAuditJobEnqueuer jobEnqueuer,
    IHttpContextAccessor httpContextAccessor,
    ICurrentUser currentUser,
    ICrossModuleUnitOfWork uow,
    IGuidGenerator guidGenerator,
    AuditEntryPersister persister,
    ILogger<AuditService> logger) : IAuditService, IAuditLogger
{
    public Task LogAsync(
        string action,
        string moduleName,
        string resourceType,
        string? resourceId = null,
        string? dataSnapshot = null,
        CancellationToken cancellationToken = default) =>
        LogInternalAsync(action, moduleName, resourceType, resourceId, dataSnapshot,
            commandType: null, tenantIdOverride: null, cancellationToken);

    private async Task LogInternalAsync(
        string action,
        string moduleName,
        string resourceType,
        string? resourceId,
        string? dataSnapshot,
        Type? commandType,
        Guid? tenantIdOverride,
        CancellationToken cancellationToken)
    {
        // ---- Capture phase: ALL request-scoped ambient reads happen here, on the request thread.
        // Everything the durable retry might later need is frozen into `entry` — the background job
        // has no HttpContext/principal, so nothing below this point may re-read them. ----
        var ctx = httpContextAccessor.HttpContext;
        var ipAddress = ctx?.Connection.RemoteIpAddress?.ToString();
        var correlationId = ctx?.TraceIdentifier;

        // QĐ 742 §3.1.b — derive (Category, Severity) from the action string + commandType.
        // [AuditCategory] attribute on the MediatR command type wins over verb fallback.
        var (category, severity) = AuditCategoryMapper.Map(action, commandType);

        // A system-to-system command (no request principal) can attribute the row to the tenant it
        // derived from a persisted entity, so the row lands on that tenant's HMAC chain rather than the
        // null/system chain. The actor (user) stays the ambient principal — the system performed the row.
        var tenantId = tenantIdOverride ?? currentUser.TenantId;

        var entry = AuditLogEntry.Create(
            guidGenerator,
            tenantId,
            currentUser.UserId,
            currentUser.UserName,
            action,
            moduleName,
            resourceType,
            resourceId,
            dataSnapshot,
            ipAddress,
            correlationId,
            category,
            severity);

        // ---- Persist phase ----
        // NOTE: `cancellationToken` is intentionally NOT threaded into the write span — the persist runs
        // AFTER the business command committed, so a request-abort must not drop the audit row (see
        // AuditEntryPersister, which pins the span to CancellationToken.None). The parameter is kept for
        // the IAuditLogger signature and future read-side use.
        //
        // Nested caller (audit logged from WITHIN a still-open outer UoW — dormant today; AuditBehavior is
        // always standalone) must let its failure propagate to that enclosing UoW owner so the outer
        // transaction rolls back atomically; resurrecting that audit via a standalone retry would record an
        // op the outer tx undid. Only the standalone request path — every caller today — gets the durable
        // fallback.
        if (uow.IsActive)
        {
            await persister.PersistAsync(entry);
            return;
        }

        try
        {
            await persister.PersistAsync(entry);
        }
        catch (Exception ex)
        {
            // The business command already committed (AuditBehavior fires post-commit), so dropping the
            // audit row here is the cell #7 L1 ingestion-loss. Hand the frozen entry to a durable Hangfire
            // retry instead of swallowing it — the row is now pending-recoverable, not lost.
            var moduleTag = new KeyValuePair<string, object?>("module", moduleName);
            try
            {
                jobEnqueuer.EnqueueIngestionRetry(AuditIngestionIntent.FromEntry(entry));
                AuditMetrics.IngestionFallbackEnqueued.Add(1, moduleTag);
                logger.LogWarning(ex,
                    "Audit persist failed for {Action}/{Module}; enqueued durable retry {MessageId}.",
                    action, moduleName, entry.MessageId);
            }
            catch (Exception enqueueEx)
            {
                // Double failure: the persist AND the durable enqueue both died → the audit event is
                // truly lost. Count it (the alert-worthy signal) and rethrow so AuditBehavior logs Critical.
                AuditMetrics.IngestionLost.Add(1, moduleTag);
                throw new AggregateException(
                    "Audit persist failed and the durable retry enqueue also failed — audit event lost.",
                    ex, enqueueEx);
            }
        }
    }

    public async Task LogPersonalDataAccessAsync(
        Guid dataSubjectId,
        string dataCategory,
        string purpose,
        string legalBasis,
        CancellationToken cancellationToken = default)
    {
        var log = PersonalDataProcessingLog.Record(
            guidGenerator,
            currentUser.TenantId ?? Guid.Empty,
            currentUser.UserId,
            dataSubjectId,
            dataCategory,
            purpose,
            legalBasis,
            processingAction: "access");

        db.PersonalDataProcessingLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task LogLoginAttemptAsync(
        Guid? userId, string email, string ipAddress, string? userAgent,
        bool success, string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = LoginAttempt.Record(guidGenerator, userId, email, ipAddress, userAgent, success, failureReason);
        db.LoginAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
    }

    // IAuditLogger — called by MediatR AuditBehavior (auto) and by handlers that suppress auto-audit.
    public Task LogCommandAsync(
        string action, string moduleName, string resourceType,
        string? resourceId = null, Type? commandType = null,
        string? dataSnapshot = null,
        Guid? tenantIdOverride = null,
        CancellationToken cancellationToken = default) =>
        LogInternalAsync(action, moduleName, resourceType, resourceId, dataSnapshot,
            commandType, tenantIdOverride, cancellationToken);
}
