using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Application.Services;

/// <summary>
/// Durable, self-contained snapshot of an audit entry that failed its synchronous write, handed to
/// <see cref="IBackgroundAuditJobEnqueuer.EnqueueIngestionRetry"/> so the audit row is recovered by
/// a background retry instead of being silently lost (cell #7 L1 ingestion-loss).
///
/// EVERY field the retry job needs to rebuild the row is frozen here at request time — the job runs
/// WITHOUT an HTTP context or request principal, so it must never re-read <c>IHttpContextAccessor</c>
/// or <c>ICurrentUser</c> (doing so would attribute the row to the null/system tenant chain instead
/// of the caller's — a silent misattribution worse than the loss).
///
/// Plain mutable class (public get/set, parameterless ctor) so Hangfire's Newtonsoft serializer can
/// round-trip it through the job store — NOT a positional record (see repo convention
/// <c>feedback-dapper-no-positional-records</c> for the same serializer caveat).
/// </summary>
public sealed class AuditIngestionIntent
{
    public Guid MessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string? DataSnapshot { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public AuditCategory Category { get; set; } = AuditCategory.AppAccess;
    public AuditSeverity Severity { get; set; } = AuditSeverity.Low;
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Freeze a built (not-yet-persisted) entry into a serializable retry intent.</summary>
    public static AuditIngestionIntent FromEntry(AuditLogEntry entry) => new()
    {
        MessageId = entry.MessageId,
        CreatedAt = entry.CreatedAt,
        TenantId = entry.TenantId,
        UserId = entry.UserId,
        UserName = entry.UserName,
        Action = entry.Action,
        ModuleName = entry.ModuleName,
        ResourceType = entry.ResourceType,
        ResourceId = entry.ResourceId,
        DataSnapshot = entry.DataSnapshot,
        IpAddress = entry.IpAddress,
        CorrelationId = entry.CorrelationId,
        OccurredAt = entry.OccurredAt,
        Category = entry.Category,
        Severity = entry.Severity,
        SchemaVersion = entry.SchemaVersion
    };

    /// <summary>Rebuild the audit entry from this intent for the durable retry insert.</summary>
    public AuditLogEntry ToEntry() => AuditLogEntry.Rehydrate(
        MessageId, CreatedAt, TenantId, UserId, UserName, Action, ModuleName, ResourceType,
        ResourceId, DataSnapshot, IpAddress, CorrelationId, OccurredAt, Category, Severity, SchemaVersion);
}
