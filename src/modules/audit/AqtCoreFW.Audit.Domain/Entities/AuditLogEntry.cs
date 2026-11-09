using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// HMAC-chained audit log entry — tamper-resistant, append-only.
/// HMAC chain: HmacSignature = HMAC(PreviousHash + EntityJson).
/// NĐ53: 12-month hot retention, archive after.
/// Law 91/2025: logs WHO accessed WHAT PII, WHEN, for WHAT purpose.
/// QĐ 742 mục 3.1.b: every entry classified into one of five Categories with a Severity rating.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index, used for outbox dedup
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
///
/// DEVIATION: SetHmacSignature() / SetSequenceId() mutation helpers preserved — required by
/// HmacAuditService for HMAC chain computation post-creation. Not a state machine transition;
/// Phase-3 R3 invariant NOT violated (MarkDeleted/Delete not exposed).
/// </summary>
public sealed class AuditLogEntry : IMessageIdentified
{
    private AuditLogEntry() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>TenantId — stays Guid (FK exception list).</summary>
    public Guid? TenantId { get; private set; }

    [DataClassification(DataClassificationLevel.Internal)]
    /// <summary>UserId — stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid? UserId { get; private set; }

    [DataClassification(DataClassificationLevel.Confidential)]
    public string UserName { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string ModuleName { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string? ResourceId { get; private set; }

    /// <summary>JSON snapshot of changed data — no raw PII, only IDs and non-sensitive fields.</summary>
    public string? DataSnapshot { get; private set; }

    [DataClassification(DataClassificationLevel.Confidential)]
    public string? IpAddress { get; private set; }

    [DataClassification(DataClassificationLevel.Confidential)]
    public string? UserAgent { get; private set; }

    /// <summary>Domain event timestamp — distinct from CreatedAt (T3 PK timestamp).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>HMAC of (PreviousHash + current entry data) — tamper detection.</summary>
    public string HmacSignature { get; private set; } = string.Empty;
    public string? CorrelationId { get; private set; }
    public long SequenceId { get; private set; }

    /// <summary>QĐ 742 mục 3.1.b — five-group classification. Default <see cref="AuditCategory.AppAccess"/>.</summary>
    public AuditCategory Category { get; private set; } = AuditCategory.AppAccess;

    /// <summary>Severity rating reused from the existing 4-value <see cref="AuditSeverity"/> scale (Low/Medium/High/Critical).</summary>
    public AuditSeverity Severity { get; private set; } = AuditSeverity.Low;

    /// <summary>
    /// HMAC payload schema version. v1 (legacy) signs without Category/Severity;
    /// v2 (current) includes them. Verify-chain endpoints branch on this field.
    /// </summary>
    public int SchemaVersion { get; private set; } = 2;

    public static AuditLogEntry Create(
        IGuidGenerator gen,
        Guid? tenantId,
        Guid? userId,
        string userName,
        string action,
        string moduleName,
        string resourceType,
        string? resourceId,
        string? dataSnapshot,
        string? ipAddress,
        string? correlationId,
        AuditCategory category = AuditCategory.AppAccess,
        AuditSeverity severity = AuditSeverity.Low,
        int schemaVersion = 2)
    {
        return new AuditLogEntry
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            TenantId = tenantId,
            UserId = userId,
            UserName = userName,
            Action = action,
            ModuleName = moduleName,
            ResourceType = resourceType,
            ResourceId = resourceId,
            DataSnapshot = dataSnapshot,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            OccurredAt = DateTimeOffset.UtcNow,
            HmacSignature = string.Empty, // set by HmacAuditService after chain computation
            Category = category,
            Severity = severity,
            SchemaVersion = schemaVersion
        };
    }

    /// <summary>
    /// Reconstruct an entry from a durable retry intent — preserves the ORIGINAL identity and
    /// timestamps (MessageId, OccurredAt, CreatedAt) that were captured on the request thread,
    /// unlike <see cref="Create"/> which mints fresh ones. Used ONLY by the audit-ingestion retry
    /// path (AuditIngestionRetryJob) so a re-inserted row keeps its original AK (dedup) and its
    /// original chronology on the HMAC chain. SequenceId stays 0 / HmacSignature empty — the row is
    /// signed by the normal per-tenant chain pass after insert, identical to the sync path.
    /// </summary>
    public static AuditLogEntry Rehydrate(
        Guid messageId,
        DateTime createdAt,
        Guid? tenantId,
        Guid? userId,
        string userName,
        string action,
        string moduleName,
        string resourceType,
        string? resourceId,
        string? dataSnapshot,
        string? ipAddress,
        string? correlationId,
        DateTimeOffset occurredAt,
        AuditCategory category,
        AuditSeverity severity,
        int schemaVersion)
    {
        return new AuditLogEntry
        {
            MessageId = messageId,
            CreatedAt = createdAt,
            TenantId = tenantId,
            UserId = userId,
            UserName = userName,
            Action = action,
            ModuleName = moduleName,
            ResourceType = resourceType,
            ResourceId = resourceId,
            DataSnapshot = dataSnapshot,
            IpAddress = ipAddress,
            CorrelationId = correlationId,
            OccurredAt = occurredAt,
            HmacSignature = string.Empty, // set by the chain pass after insert, same as Create
            Category = category,
            Severity = severity,
            SchemaVersion = schemaVersion
        };
    }

    /// <summary>DEVIATION: post-creation mutation by HmacAuditService for HMAC chain computation.</summary>
    public void SetHmacSignature(string signature) => HmacSignature = signature;

    /// <summary>DEVIATION: post-creation mutation by sequence assignment service.</summary>
    public void SetSequenceId(long id) => SequenceId = id;
}
