using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Write-side repository for WorkflowTransitionAttachment.
/// Used by ExecuteTransitionCommandHandler to persist file references after a transition.
/// </summary>
public interface ITransitionAttachmentRepository
{
    /// <summary>Persists a batch of attachments linked to a history entry.</summary>
    Task AddRangeAsync(
        IEnumerable<WorkflowTransitionAttachment> attachments,
        CancellationToken cancellationToken = default);
}
