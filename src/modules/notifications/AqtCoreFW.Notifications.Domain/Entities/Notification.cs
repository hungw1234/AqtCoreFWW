using AqtCoreFW.Notifications.Domain.Events;
using AqtCoreFW.Notifications.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Notifications.Domain.Entities;

/// <summary>
/// Notification aggregate root — in-app/email/sms notification record.
///
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// TenantId/RecipientUserId: Guid (FK exception list — tenant isolation + identity ref).
/// TemplateId: long? (FK to T1 NotificationTemplate.Id internal PK — changed from Guid?).
/// Phase-02c: NotificationCreatedEvent MessageId caller-supplied via gen (Rule1 compliant).
/// </summary>
public sealed class Notification : AuditableEntity, IPublicIdentified, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }
    [DataClassification(DataClassificationLevel.Internal)]
    public Guid RecipientUserId { get; private set; }
    /// <summary>Notification subject — may contain rendered PII (e.g. recipient name from template).</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string Subject { get; private set; } = string.Empty;
    /// <summary>Notification body — may contain rendered PII from template substitution.</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string Body { get; private set; } = string.Empty;
    public NotificationChannel Channel { get; private set; } = default!;
    public NotificationStatus Status { get; private set; } = NotificationStatus.Pending;
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Optional — links notification to a template for audit trail. FK to NotificationTemplate.Id (long).</summary>
    public long? TemplateId { get; private set; }

    /// <summary>Correlation ID from originating event (e.g. CaseId). Used for dedup.</summary>
    public string? CorrelationId { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Notification() { } // EF Core constructor

    public static Notification Create(
        IGuidGenerator gen,
        Guid tenantId,
        Guid recipientUserId,
        string subject,
        string body,
        NotificationChannel channel,
        Guid createdBy,
        long? templateId = null,
        string? correlationId = null)
    {
        var notification = new Notification
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            RecipientUserId = recipientUserId,
            Subject = subject,
            Body = body,
            Channel = channel,
            Status = NotificationStatus.Pending,
            TemplateId = templateId,
            CorrelationId = correlationId
        };
        notification.SetAuditCreate(createdBy);
        // Event carries PublicId (T2 AK) — not internal Id. MessageId from gen (Rule1 compliant).
        notification.AddDomainEvent(new NotificationCreatedEvent(
            notification.PublicId, tenantId, recipientUserId, channel.Value, gen.NewIdRandom()));
        return notification;
    }

    public void MarkAsSent()
    {
        Status = NotificationStatus.Sent;
        SentAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void MarkAsFailed()
    {
        Status = NotificationStatus.Failed;
        MarkUpdated();
    }

    public void MarkAsRead()
    {
        if (IsRead) return;
        IsRead = true;
        Status = NotificationStatus.Read;
        ReadAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
