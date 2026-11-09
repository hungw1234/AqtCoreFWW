using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Integration.Domain.Entities;

/// <summary>
/// Integration message log — records inbound/outbound message exchanges with partners.
/// State transitions: Sent → Delivered | Failed; Sent | Delivered → Acknowledged.
///
/// T3 plain-class (AMEND-A3): IMessageIdentified + long InternalId PK + Guid MessageId AK.
/// Preserves MarkDelivered/MarkFailed/Acknowledge status mutators (mutable audit log pattern,
/// per B3 RtbfRequest/SecurityIncident precedent). No MarkDeleted/IsDeleted — Phase-3 R3 passes trivially.
/// Manual IEntityTypeConfiguration required (cannot inherit HotTableConfigurationBase — expects Entity base).
///
/// TenantId: Guid (FK exception list — tenant isolation key).
/// PartnerId: long (FK to T1 Partner.Id internal PK).
/// ContractId: long? (FK to T2 Contract.Id internal PK — nullable).
/// CreatedBy (via Guid createdBy param): stored in CreatedByUserId for audit — Guid (identity FK exception).
/// </summary>
public sealed class MessageLog : IMessageIdentified, ITenantScoped
{
    /// <summary>bigint identity PK, DB-assigned post-INSERT. 0 until SaveChanges.</summary>
    public long InternalId { get; private set; }

    /// <summary>v7 monotonic Guid AK — set at create time for idempotency and correlation.</summary>
    public Guid MessageId { get; private set; }

    public Guid TenantId { get; private set; }
    /// <summary>FK to Partner.Id (long, T1 internal PK).</summary>
    public long PartnerId { get; private set; }
    /// <summary>FK to Contract.Id (long, T2 internal PK — nullable).</summary>
    public long? ContractId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public string MessageType { get; private set; } = string.Empty;
    public string? Payload { get; private set; }
    public MessageLogStatus Status { get; private set; } = MessageLogStatus.Sent;
    public string? CorrelationId { get; private set; }
    public DateTime SentAt { get; private set; }
    public DateTime? AcknowledgedAt { get; private set; }
    /// <summary>Creator identity ref — Guid (FK exception list — AspNetUsers.Id).</summary>
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private MessageLog() { } // EF Core

    public static MessageLog Create(
        IGuidGenerator gen,
        Guid tenantId,
        long partnerId,
        long? contractId,
        MessageDirection direction,
        string messageType,
        string? payload,
        string? correlationId,
        Guid createdBy)
    {
        return new MessageLog
        {
            MessageId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            PartnerId = partnerId,
            ContractId = contractId,
            Direction = direction,
            MessageType = messageType,
            Payload = payload,
            CorrelationId = correlationId,
            Status = MessageLogStatus.Sent,
            SentAt = DateTime.UtcNow,
            CreatedByUserId = createdBy,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Transitions status from Sent → Delivered.</summary>
    public void MarkDelivered()
    {
        if (Status != MessageLogStatus.Sent)
            throw new InvalidOperationException($"Cannot mark delivered from {Status} state.");
        Status = MessageLogStatus.Delivered;
    }

    /// <summary>Transitions status to Failed (allowed from Sent or Delivered, not Acknowledged).</summary>
    public void MarkFailed()
    {
        if (Status is MessageLogStatus.Acknowledged)
            throw new InvalidOperationException("Cannot fail an acknowledged message.");
        Status = MessageLogStatus.Failed;
    }

    /// <summary>Transitions status from Sent or Delivered → Acknowledged.</summary>
    public void Acknowledge()
    {
        if (Status is not (MessageLogStatus.Sent or MessageLogStatus.Delivered))
            throw new InvalidOperationException($"Cannot acknowledge from {Status} state.");
        Status = MessageLogStatus.Acknowledged;
        AcknowledgedAt = DateTime.UtcNow;
    }
}
