namespace AqtCoreFW.Forms.Domain.Enums;

/// <summary>
/// Lifecycle status of a <see cref="Entities.FormSubmissionWorkflowLinkPendingAttempt"/>.
/// Stored as string column (HasConversion<string>) for human-readable DB inspection.
/// </summary>
public enum WorkflowLinkPendingStatus
{
    /// <summary>Awaiting first retry attempt.</summary>
    Pending = 0,

    /// <summary>An automated retry job is in progress.</summary>
    Retrying = 1,

    /// <summary>Workflow instance successfully created; orphan resolved.</summary>
    Succeeded = 2,

    /// <summary>Max retry attempts exceeded; admin intervention required (dead-letter).</summary>
    Shelved = 3
}
