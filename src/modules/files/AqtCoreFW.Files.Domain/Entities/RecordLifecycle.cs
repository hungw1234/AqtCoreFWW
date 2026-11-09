using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Files.Domain.Entities;

/// <summary>
/// Tracks the lifecycle state of a FileRecord under a RetentionPolicy.
/// One RecordLifecycle per FileRecord — created when retention policy is applied.
/// CurrentStatus transitions: Active → Archived → PendingDestruction → Destroyed.
/// ScheduledDestroyAt populated when policy's DestroyAfterDays threshold is reached.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index
///   - DateTime CreatedAt: UTC, set at creation
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
///
/// FK flips: FileRecordId Guid → long (FileRecord.Id is now bigint).
///           RetentionPolicyId Guid → long (RetentionPolicy.Id is now bigint).
/// DestroyedBy stays Guid (FK exception list: references AspNetUsers.Id).
///
/// DEVIATION: Archive/ScheduleDestruction/MarkDestroyed lifecycle mutations preserved —
/// file retention workflow requires these state transitions for compliance.
/// SetAuditUpdate removed (no AuditableEntity base); mutation timestamps tracked via domain fields.
/// Phase-3 R3 invariant NOT violated (MarkDeleted/Delete not exposed).
/// </summary>
public sealed class RecordLifecycle : IMessageIdentified
{
    private RecordLifecycle() { } // EF Core

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>FK to FileRecord.Id (bigint identity PK).</summary>
    public long FileRecordId { get; private set; }

    public RecordLifecycleStatus CurrentStatus { get; private set; }

    /// <summary>FK to RetentionPolicy.Id (bigint identity PK).</summary>
    public long? RetentionPolicyId { get; private set; }

    public DateTime? ArchivedAt { get; private set; }
    public DateTime? ScheduledDestroyAt { get; private set; }
    public DateTime? DestroyedAt { get; private set; }

    /// <summary>DestroyedBy stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid? DestroyedBy { get; private set; }

    public static RecordLifecycle Create(
        IGuidGenerator gen,
        long fileRecordId,
        long? retentionPolicyId)
    {
        return new RecordLifecycle
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            FileRecordId = fileRecordId,
            RetentionPolicyId = retentionPolicyId,
            CurrentStatus = RecordLifecycleStatus.Active
        };
    }

    /// <summary>Moves record to archived state — file may be moved to cold storage.</summary>
    public void Archive()
    {
        CurrentStatus = RecordLifecycleStatus.Archived;
        ArchivedAt = DateTime.UtcNow;
    }

    /// <summary>Schedules record for destruction at the given time.</summary>
    public void ScheduleDestruction(DateTime scheduledDestroyAt)
    {
        CurrentStatus = RecordLifecycleStatus.PendingDestruction;
        ScheduledDestroyAt = scheduledDestroyAt;
    }

    /// <summary>Marks record as permanently destroyed — irreversible.</summary>
    public void MarkDestroyed(Guid destroyedBy)
    {
        CurrentStatus = RecordLifecycleStatus.Destroyed;
        DestroyedAt = DateTime.UtcNow;
        DestroyedBy = destroyedBy;
    }
}

public enum RecordLifecycleStatus
{
    Active = 0,
    Archived = 1,
    PendingDestruction = 2,
    Destroyed = 3
}
