using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Audit record of every assignment event for a WorkflowTask.
/// Tracks who assigned to whom, when, why, and the assignment type
/// (Initial, Reassign, Delegate, Escalation) for audit trail and escalation history.
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   WorkflowTaskId: long (FK to T1 WorkflowTask.Id).
///   AssignedById: Guid (FK exception — user ref).
///   AssignedToId: Guid (FK exception — user ref).
/// </summary>
public sealed class TaskAssignment : Entity
{
    public long WorkflowTaskId { get; private set; }
    public Guid AssignedById { get; private set; }
    public Guid AssignedToId { get; private set; }
    public DateTime AssignedAt { get; private set; }
    public string? Reason { get; private set; }
    public TaskAssignmentType Type { get; private set; }

    private TaskAssignment() { } // EF Core

    public static TaskAssignment Create(
        long workflowTaskId,
        Guid assignedById,
        Guid assignedToId,
        TaskAssignmentType type,
        string? reason = null)
    {
        return new TaskAssignment
        {
            WorkflowTaskId = workflowTaskId,
            AssignedById = assignedById,
            AssignedToId = assignedToId,
            AssignedAt = DateTime.UtcNow,
            Type = type,
            Reason = reason
        };
    }
}

public enum TaskAssignmentType
{
    Initial = 0,
    Reassign = 1,
    Delegate = 2,
    Escalation = 3,
    /// <summary>Delegation returned to original assignee — manually or by expiry job.</summary>
    Return = 4,
    /// <summary>Pool task claimed by a pool-eligible member (plan 260519-0421 v2, P4).</summary>
    Claim = 5,
    /// <summary>Claimed pool task released back to the pool by its assignee.</summary>
    Unclaim = 6,
    /// <summary>
    /// Admin-stomp: an admin overwrites an already-claimed task (AssigneeId was non-null).
    /// Distinct from Reassign to allow audit ledger to distinguish governance overrides.
    /// Requires a non-empty Reason — enforced in ReassignTaskCommandHandler.
    /// </summary>
    ReassignAdmin = 7
}
