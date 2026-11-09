using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Per-instance idempotency guard for WorkflowVersionMigration.
/// UNIQUE (MigrationId, InstanceId) prevents Hangfire retry from duplicating history.
/// Child record — no soft-delete, no auditing. Owned by parent migration (cascade delete).
///
/// T3 plain-class (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index, used for outbox dedup
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted
///   - Implements IMessageIdentified
///   - MigrationId: long (FK to T1 WorkflowVersionMigration.Id)
///   - InstanceId: long (FK to T2 WorkflowInstance.Id)
///   - OldStateId: long, NewStateId: long (FKs to T1 WorkflowState.Id)
///
/// DEVIATION (AMEND-A3): MarkSucceeded/MarkFailed/MarkSkipped mutation helpers preserved —
/// required by WorkflowMigrationExecutor for per-item status tracking post-creation.
/// Not a state machine transition; R3 invariant NOT violated (MarkDeleted not exposed).
/// </summary>
public sealed class WorkflowVersionMigrationItem : IMessageIdentified
{
    public long InternalId { get; private set; }
    public Guid MessageId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>FK to parent WorkflowVersionMigration (cascade delete).</summary>
    public long MigrationId { get; private set; }

    public long InstanceId { get; private set; }
    public long OldStateId { get; private set; }
    public long NewStateId { get; private set; }
    public MigrationItemStatus Status { get; private set; }

    /// <summary>Error or skip reason. Max 1000 chars (enforced in EF config).</summary>
    public string? ErrorMessage { get; private set; }

    public int AttemptCount { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>EF Core concurrency token.</summary>
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowVersionMigrationItem() { } // EF Core

    public static WorkflowVersionMigrationItem Create(
        IGuidGenerator gen,
        long migrationId,
        long instanceId,
        long oldStateId,
        long newStateId)
    {
        if (migrationId <= 0)
            throw new ArgumentException("MigrationId must be positive.", nameof(migrationId));
        if (instanceId <= 0)
            throw new ArgumentException("InstanceId must be positive.", nameof(instanceId));

        return new WorkflowVersionMigrationItem
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            MigrationId = migrationId,
            InstanceId = instanceId,
            OldStateId = oldStateId,
            NewStateId = newStateId,
            Status = MigrationItemStatus.Pending,
            AttemptCount = 0
        };
    }

    /// <summary>
    /// DEVIATION (AMEND-A3): post-creation mutation by WorkflowMigrationExecutor.
    /// Mark this item as successfully migrated. Idempotent if already Succeeded.
    /// </summary>
    public void MarkSucceeded()
    {
        if (Status == MigrationItemStatus.Succeeded) return; // Hangfire retry safety

        Status = MigrationItemStatus.Succeeded;
        AttemptCount++;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// DEVIATION (AMEND-A3): post-creation mutation by WorkflowMigrationExecutor.
    /// Mark this item as failed with a reason (e.g. "unmapped state").
    /// </summary>
    public void MarkFailed(string errorMessage)
    {
        Status = MigrationItemStatus.Failed;
        ErrorMessage = errorMessage;
        AttemptCount++;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// DEVIATION (AMEND-A3): post-creation mutation by WorkflowMigrationExecutor.
    /// Mark this item as skipped (e.g. "concurrent transition").
    /// </summary>
    public void MarkSkipped(string reason)
    {
        Status = MigrationItemStatus.Skipped;
        ErrorMessage = reason;
        ProcessedAt = DateTimeOffset.UtcNow;
    }
}
