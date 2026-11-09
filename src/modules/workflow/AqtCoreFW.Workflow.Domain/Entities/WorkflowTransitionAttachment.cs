using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Files attached during a workflow transition execution.
/// InstanceHistoryId links to the specific transition history entry.
/// FileId links to the Files module FileRecord (external reference — no FK enforced here).
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   InstanceHistoryId: long (FK to T3 WorkflowInstanceHistory.InternalId).
///   FileId: Guid — references Files module FileRecord.PublicId (T2 AK); stays Guid (cross-module AK ref).
/// </summary>
public sealed class WorkflowTransitionAttachment : Entity
{
    public long InstanceHistoryId { get; private set; }   // links to WorkflowInstanceHistory
    public Guid FileId { get; private set; }              // links to Files module FileRecord.PublicId
    public string FileName { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private WorkflowTransitionAttachment() { } // EF Core

    public static WorkflowTransitionAttachment Create(
        long historyId,
        Guid fileId,
        string fileName,
        string? description)
    {
        return new WorkflowTransitionAttachment
        {
            InstanceHistoryId = historyId,
            FileId = fileId,
            FileName = fileName,
            Description = description
        };
    }
}
