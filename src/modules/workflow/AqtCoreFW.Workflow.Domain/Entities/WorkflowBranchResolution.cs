using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Defines a possible outcome for a WorkflowBranchChild.
/// ResultingStateId is optional: when set, the instance moves to that state on this resolution type.
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   BranchChildId: long (FK to T1 WorkflowBranchChild.Id).
///   ResultingStateId: long? (FK to T1 WorkflowState.Id).
/// </summary>
public sealed class WorkflowBranchResolution : Entity
{
    public long BranchChildId { get; private set; }

    public BranchResolutionType ResolutionType { get; private set; }

    /// <summary>
    /// Optional redirect state for this specific resolution outcome.
    /// Null means the parent join logic determines the next state.
    /// </summary>
    public long? ResultingStateId { get; private set; }

    private WorkflowBranchResolution() { } // EF Core

    public static WorkflowBranchResolution Create(
        long branchChildId,
        BranchResolutionType resolutionType,
        long? resultingStateId)
    {
        return new WorkflowBranchResolution
        {
            BranchChildId = branchChildId,
            ResolutionType = resolutionType,
            ResultingStateId = resultingStateId
        };
    }
}
