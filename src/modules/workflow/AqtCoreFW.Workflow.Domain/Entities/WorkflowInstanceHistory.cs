using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Immutable audit record of a single state transition on a WorkflowInstance.
/// Append-only — never updated after creation.
///
/// T3 plain-class (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index, used for outbox dedup
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - InstanceId: long (FK to T2 WorkflowInstance.Id)
///   - FromStateId: long, ToStateId: long (FKs to T1 WorkflowState.Id)
///   - TransitionedBy: Guid (FK exception — user ref)
/// </summary>
public sealed class WorkflowInstanceHistory : IMessageIdentified
{
    public long InternalId { get; private set; }
    public Guid MessageId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public long InstanceId { get; private set; }
    public long FromStateId { get; private set; }
    public long ToStateId { get; private set; }
    public string ActionName { get; private set; } = string.Empty;
    public Guid TransitionedBy { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset TransitionedAt { get; private set; }

    private WorkflowInstanceHistory() { } // EF Core

    public static WorkflowInstanceHistory Create(
        IGuidGenerator gen,
        long instanceId,
        long fromStateId,
        long toStateId,
        string actionName,
        Guid transitionedBy,
        string? comment,
        DateTimeOffset transitionedAt)
    {
        return new WorkflowInstanceHistory
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            InstanceId = instanceId,
            FromStateId = fromStateId,
            ToStateId = toStateId,
            ActionName = actionName,
            TransitionedBy = transitionedBy,
            Comment = comment,
            TransitionedAt = transitionedAt
        };
    }
}
