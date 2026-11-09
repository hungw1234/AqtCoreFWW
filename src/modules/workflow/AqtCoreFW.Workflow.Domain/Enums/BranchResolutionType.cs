namespace AqtCoreFW.Workflow.Domain.Enums;

/// <summary>
/// Possible outcomes when a branch child is resolved.
/// Approved/Rejected/Escalated drive the join logic; Timeout is set by the scheduler.
/// </summary>
public enum BranchResolutionType
{
    Approved = 0,
    Rejected = 1,
    Escalated = 2,
    Timeout = 3
}
