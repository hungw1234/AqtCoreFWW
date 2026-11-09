using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Defines a parallel branching configuration within a workflow definition.
/// BranchingStateId = state that triggers fan-out; ContinuationStateId = state after join completes.
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   DefinitionId: long, BranchingStateId: long, ContinuationStateId: long (FKs to T1 entities).
/// </summary>
public sealed class WorkflowParallelBranch : Entity
{
    private readonly List<WorkflowBranchChild> _children = [];

    public long DefinitionId { get; private set; }

    /// <summary>The state that triggers fan-out into parallel branches.</summary>
    public long BranchingStateId { get; private set; }

    /// <summary>The state the instance transitions to after all branches join.</summary>
    public long ContinuationStateId { get; private set; }

    public ParallelBranchType BranchType { get; private set; }

    /// <summary>Required number of approvals for NOfM join strategy. Null for And/Or.</summary>
    public int? RequiredCount { get; private set; }

    /// <summary>When true and any child rejects, cancels remaining siblings and fails the branch.</summary>
    public bool AllowCancelOnAnyReject { get; private set; }

    /// <summary>Minutes until the branch times out. Zero means no timeout.</summary>
    public int TimeoutMinutes { get; private set; }

    /// <summary>
    /// Canvas X coordinate of the single draggable fork gateway diamond. Null ⇒ the visual
    /// designer auto-derives an initial position from the branching/child state coordinates.
    /// The continuation state is the visual join, so only the fork has a persisted position.
    /// </summary>
    public double? PositionX { get; private set; }

    /// <summary>Canvas Y coordinate of the fork gateway diamond. See <see cref="PositionX"/>.</summary>
    public double? PositionY { get; private set; }

    public IReadOnlyList<WorkflowBranchChild> Children => _children.AsReadOnly();

    private WorkflowParallelBranch() { } // EF Core

    public static WorkflowParallelBranch Create(
        long definitionId,
        long branchingStateId,
        long continuationStateId,
        ParallelBranchType branchType,
        int? requiredCount,
        bool allowCancelOnAnyReject,
        int timeoutMinutes,
        double? positionX = null,
        double? positionY = null)
    {
        return new WorkflowParallelBranch
        {
            DefinitionId = definitionId,
            BranchingStateId = branchingStateId,
            ContinuationStateId = continuationStateId,
            BranchType = branchType,
            RequiredCount = requiredCount,
            AllowCancelOnAnyReject = allowCancelOnAnyReject,
            TimeoutMinutes = timeoutMinutes,
            PositionX = positionX,
            PositionY = positionY
        };
    }

    public void Update(
        long branchingStateId,
        long continuationStateId,
        ParallelBranchType branchType,
        int? requiredCount,
        bool allowCancelOnAnyReject,
        int timeoutMinutes,
        double? positionX = null,
        double? positionY = null)
    {
        BranchingStateId = branchingStateId;
        ContinuationStateId = continuationStateId;
        BranchType = branchType;
        RequiredCount = requiredCount;
        AllowCancelOnAnyReject = allowCancelOnAnyReject;
        TimeoutMinutes = timeoutMinutes;
        PositionX = positionX;
        PositionY = positionY;
        MarkUpdated();
    }

    public void AddChild(WorkflowBranchChild child) => _children.Add(child);
}
