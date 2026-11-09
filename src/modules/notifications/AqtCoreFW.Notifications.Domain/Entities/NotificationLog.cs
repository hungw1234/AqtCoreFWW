using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Notifications.Domain.Entities;

/// <summary>
/// Idempotency guard — unique index on (TemplateId, RecipientId, CorrelationId).
/// Prevents double-send on retry storms (S52 dedup spec).
///
/// T3 plain-class (AMEND-A3): pure append-only, no status mutators.
/// long InternalId PK + Guid MessageId AK (IMessageIdentified).
/// No MarkDeleted/IsDeleted — Phase-3 R3 passes trivially.
/// Manual IEntityTypeConfiguration required (cannot inherit HotTableConfigurationBase).
///
/// TemplateId: long (FK to T1 NotificationTemplate.Id internal PK — changed from Guid).
/// RecipientId: Guid (FK exception list — identity ref to AspNetUsers.Id).
/// </summary>
public sealed class NotificationLog : IMessageIdentified
{
    /// <summary>bigint identity PK, DB-assigned post-INSERT. 0 until SaveChanges.</summary>
    public long InternalId { get; private set; }

    /// <summary>v7 monotonic Guid AK — set at create time for idempotency correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>FK to NotificationTemplate.Id (long, T1 internal PK).</summary>
    public long TemplateId { get; private set; }

    /// <summary>FK to AspNetUsers.Id (Guid — identity FK exception list).</summary>
    public Guid RecipientId { get; private set; }

    /// <summary>Correlation ID from originating request/event (e.g. CaseId, AuditId).</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    private NotificationLog() { } // EF Core

    public static NotificationLog Create(IGuidGenerator gen, long templateId, Guid recipientId, string correlationId)
    {
        return new NotificationLog
        {
            MessageId = gen.NewIdMonotonic(),
            TemplateId = templateId,
            RecipientId = recipientId,
            CorrelationId = correlationId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
