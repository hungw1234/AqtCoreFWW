using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Runtime tracking record for a single child branch within a WorkflowInstanceBranchStatus.
/// ResolutionType null = pending; set when Resolve() is called.
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   InstanceBranchStatusId: long, BranchChildId: long (FKs to T1 entities).
///   ResolvedBy: Guid? (FK exception — user ref).
/// </summary>
public sealed class WorkflowBranchChildStatus : Entity
{
    public long InstanceBranchStatusId { get; private set; }
    public long BranchChildId { get; private set; }

    /// <summary>Null while pending; populated when the child is resolved.</summary>
    public BranchResolutionType? ResolutionType { get; private set; }

    public Guid? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? Comment { get; private set; }

    /// <summary>Optimistic concurrency token — prevents double-resolution race (H1 fix).</summary>
    public byte[] RowVersion { get; private set; } = [];

    private WorkflowBranchChildStatus() { } // EF Core

    public static WorkflowBranchChildStatus Create(
        long instanceBranchStatusId,
        long branchChildId)
    {
        return new WorkflowBranchChildStatus
        {
            InstanceBranchStatusId = instanceBranchStatusId,
            BranchChildId = branchChildId,
            ResolutionType = null
        };
    }

    /// <summary>Record the outcome for this child branch.</summary>
    public void Resolve(BranchResolutionType resolutionType, Guid resolvedBy, string? comment)
    {
        ResolutionType = resolutionType;
        ResolvedBy = resolvedBy;
        ResolvedAt = DateTimeOffset.UtcNow;
        Comment = comment;
        MarkUpdated();
    }
}
