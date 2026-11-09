using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// Security incident record — QĐ742 mandated tracking.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
///
/// DEVIATION: UpdateStatus() mutation preserved — incident lifecycle requires status transitions
/// (Open → Investigating → Resolved → Closed) and mitigation note accumulation.
/// ReportedBy stays Guid (FK exception list: references AspNetUsers.Id).
/// Phase-3 R3 invariant NOT violated (MarkDeleted/Delete not exposed).
/// </summary>
public sealed class SecurityIncident : IMessageIdentified
{
    private SecurityIncident() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>TenantId — stays Guid (FK exception list).</summary>
    public Guid? TenantId { get; private set; }

    public string Title { get; private set; } = string.Empty;
    public AuditSeverity Severity { get; private set; }
    public IncidentStatus Status { get; private set; } = IncidentStatus.Open;
    public string Description { get; private set; } = string.Empty;

    /// <summary>ReportedBy stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid ReportedBy { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? Mitigations { get; private set; }

    public static SecurityIncident Report(
        IGuidGenerator gen,
        Guid? tenantId,
        string title,
        AuditSeverity severity,
        string description,
        Guid reportedBy,
        DateTimeOffset occurredAt)
    {
        return new SecurityIncident
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            TenantId = tenantId,
            Title = title,
            Severity = severity,
            Description = description,
            ReportedBy = reportedBy,
            OccurredAt = occurredAt
        };
    }

    /// <summary>DEVIATION: incident lifecycle mutation — UpdateStatus/Resolve/Close workflow transitions.</summary>
    public void UpdateStatus(IncidentStatus status, string? mitigationNote)
    {
        Status = status;
        if (!string.IsNullOrEmpty(mitigationNote))
            Mitigations = (Mitigations ?? string.Empty) + $"\n[{DateTimeOffset.UtcNow:u}] {mitigationNote}";
        if (status is IncidentStatus.Resolved or IncidentStatus.Closed)
            ResolvedAt = DateTimeOffset.UtcNow;
    }
}
