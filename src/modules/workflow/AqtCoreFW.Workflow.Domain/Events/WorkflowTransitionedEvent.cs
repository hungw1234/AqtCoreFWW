using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Events;

/// <summary>
/// Internal domain event raised when a WorkflowInstance transitions to a new state.
/// Consumed by Audit module to record the transition trail (NĐ13/Law 91/2025).
/// IInternalDomainEvent → dispatched via MediatR in same transaction.
/// InstancePublicId: Guid — WorkflowInstance.PublicId (T2 AK, safe to expose).
/// NewStateInternalId: long — WorkflowState.Id (T1, no AK; internal routing only).
/// </summary>
public sealed record WorkflowTransitionedEvent(
    Guid InstancePublicId,
    long NewStateInternalId,
    Guid TransitionedBy,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
