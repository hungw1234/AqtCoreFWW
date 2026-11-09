using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Runtime tracking record for a parallel branch execution on a specific workflow instance.
/// Status values: "Pending" | "InProgress" | "Resolved" | "TimedOut" | "Cancelled"
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   InstanceId: long (FK to T2 WorkflowInstance.Id).
///   ParallelBranchId: long (FK to T1 WorkflowParallelBranch.Id).
/// </summary>
public sealed class WorkflowInstanceBranchStatus : Entity
{
    /// <summary>
    /// Canonical branch-status string values. The status is persisted as text (no enum column), so
    /// every read-side filter (e.g. the fork-exit guard's "is this instance mid-fork" query) MUST
    /// reference these constants rather than re-typing the literal — a rename touching only one site
    /// would silently invert the guard.
    /// </summary>
    public static class Statuses
    {
        public const string Pending = "Pending";
        public const string InProgress = "InProgress";
        public const string Resolved = "Resolved";
        public const string TimedOut = "TimedOut";
        public const string Cancelled = "Cancelled";
    }

    private readonly List<WorkflowBranchChildStatus> _childStatuses = [];

    public long InstanceId { get; private set; }
    public long ParallelBranchId { get; private set; }

    /// <summary>One of <see cref="Statuses"/>.</summary>
    public string Status { get; private set; } = Statuses.Pending;

    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Optimistic concurrency token — prevents race conditions in parallel resolution.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<WorkflowBranchChildStatus> ChildStatuses => _childStatuses.AsReadOnly();

    private WorkflowInstanceBranchStatus() { } // EF Core

    public static WorkflowInstanceBranchStatus Create(long instanceId, long parallelBranchId)
    {
        return new WorkflowInstanceBranchStatus
        {
            InstanceId = instanceId,
            ParallelBranchId = parallelBranchId,
            Status = Statuses.Pending
        };
    }

    public void MarkInProgress()
    {
        Status = Statuses.InProgress;
        MarkUpdated();
    }

    public void MarkResolved()
    {
        Status = Statuses.Resolved;
        ResolvedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void MarkTimedOut()
    {
        Status = Statuses.TimedOut;
        ResolvedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void MarkCancelled()
    {
        Status = Statuses.Cancelled;
        ResolvedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void AddChildStatus(WorkflowBranchChildStatus childStatus) =>
        _childStatuses.Add(childStatus);
}
