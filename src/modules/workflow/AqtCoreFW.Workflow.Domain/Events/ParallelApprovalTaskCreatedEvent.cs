using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Workflow.Domain.Events;

/// <summary>
/// Raised when a WorkflowTask is auto-created for a parallel branch child.
/// Consumed by notification handler to inform assignees or broadcast for unassigned tasks.
/// TaskInternalId: long — WorkflowTask.Id (T1, no AK).
/// InstancePublicId: Guid — WorkflowInstance.PublicId (T2 AK).
/// AssigneeId: Guid? (FK exception — user ref).
/// TenantId: Guid (FK exception).
/// </summary>
public sealed record ParallelApprovalTaskCreatedEvent(
    long TaskInternalId,
    Guid InstancePublicId,
    Guid? AssigneeId,
    string TaskTitle,
    Guid TenantId,
    Guid MessageId) : IInternalDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
