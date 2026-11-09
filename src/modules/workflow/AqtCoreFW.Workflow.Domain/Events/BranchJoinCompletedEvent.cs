using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Events;

/// <summary>
/// Raised when all required branch children have resolved and the join condition is satisfied.
/// ContinuationStateInternalId is the state the instance should transition to next.
/// InstancePublicId: Guid — WorkflowInstance.PublicId (T2 AK).
/// ParallelBranchInternalId: long — WorkflowParallelBranch.Id (T1, no AK).
/// ContinuationStateInternalId: long — WorkflowState.Id (T1, no AK).
/// </summary>
public sealed record BranchJoinCompletedEvent(
    Guid InstancePublicId,
    long ParallelBranchInternalId,
    long ContinuationStateInternalId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
