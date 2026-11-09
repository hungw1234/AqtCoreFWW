using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Write-side repository for WorkflowParallelBranch and runtime branch status.
/// Exposes lookup methods needed by ExecuteTransition and ResolveBranchChild handlers.
/// Phase-4b B4c: T1 entity — IRepository&lt;T, long&gt; key is internal bigint PK.
/// </summary>
public interface IParallelBranchRepository : IRepository<WorkflowParallelBranch, long>
{
    /// <summary>
    /// Returns the branch definition configured for the given state within a definition.
    /// Returns null when no branch is configured for that state.
    /// Phase-4b B4c: definitionInternalId and stateInternalId are long (T1/T2 bigint PKs).
    /// </summary>
    Task<WorkflowParallelBranch?> GetBranchByStateIdAsync(
        long definitionInternalId,
        long stateInternalId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the active (InProgress) branch status for the instance, with child statuses loaded.
    /// Returns null if no active branch exists.
    /// Phase-4b B4c: instancePublicId = WorkflowInstance.PublicId (Guid AK).
    /// </summary>
    Task<WorkflowInstanceBranchStatus?> GetInstanceBranchStatusAsync(
        Guid instancePublicId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the branch status that contains the given child status ID, with all children loaded.
    /// Used by task completion to find the branch for auto-resolution.
    /// Phase-4b B4c: childStatusInternalId is long (T1 WorkflowBranchChildStatus.Id).
    /// </summary>
    Task<WorkflowInstanceBranchStatus?> GetBranchStatusByChildStatusIdAsync(
        long childStatusInternalId,
        CancellationToken ct = default);
}
