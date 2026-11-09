using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Events;

/// <summary>
/// Raised when parallel branching is initiated for a workflow instance.
/// ChildInternalIds are the WorkflowBranchChildStatus.Id values created for each sub-branch.
/// InstancePublicId: Guid — WorkflowInstance.PublicId (T2 AK).
/// ParallelBranchInternalId: long — WorkflowParallelBranch.Id (T1, no AK).
/// ChildInternalIds: IReadOnlyList&lt;long&gt; — WorkflowBranchChildStatus.Id values (T1).
/// </summary>
public sealed record BranchCreatedEvent(
    Guid InstancePublicId,
    long ParallelBranchInternalId,
    IReadOnlyList<long> ChildInternalIds,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
