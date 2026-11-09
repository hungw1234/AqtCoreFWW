using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// NĐ53 security investigation log — records security-relevant system events.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
/// </summary>
public sealed class SecurityEventLog : IMessageIdentified
{
    private SecurityEventLog() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>TenantId — stays Guid (FK exception list).</summary>
    public Guid? TenantId { get; private set; }

    public string EventType { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? Source { get; private set; }

    [DataClassification(DataClassificationLevel.Confidential)]
    public string? IpAddress { get; private set; }

    /// <summary>Domain event timestamp — distinct from CreatedAt (T3 PK timestamp).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public bool IsSecurityRelevant { get; private set; } = true;

    public static SecurityEventLog Create(
        IGuidGenerator gen,
        Guid? tenantId, string eventType, string description,
        string? source = null, string? ipAddress = null) =>
        new()
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            OccurredAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
            EventType = eventType,
            Description = description,
            Source = source,
            IpAddress = ipAddress
        };
}
