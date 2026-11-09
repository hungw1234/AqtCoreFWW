namespace AqtCoreFW.Workflow.Domain.Enums;

/// <summary>
/// Status of a WorkflowVersionMigration.
/// APPEND-ONLY — do not reorder or change existing values (stored as int in DB).
/// Draft=0 (reserved), PendingApproval=1 (creation entry point for v1).
/// </summary>
public enum MigrationStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    InProgress = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}
