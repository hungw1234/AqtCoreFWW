namespace AqtCoreFW.Workflow.Domain.Enums;

/// <summary>
/// Per-instance migration result.
/// APPEND-ONLY — do not reorder or change existing values.
/// </summary>
public enum MigrationItemStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3
}
