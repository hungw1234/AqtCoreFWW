namespace AqtCoreFW.Workflow.Domain.Enums;

/// <summary>
/// How multiple clauses of a single WorkflowTransitionAssignment combine into the
/// candidate pool. Cross-transition combination is always Union (resolver, P2).
/// Schema keeps both values; Intersect resolution is a NotSupported stub at MVP
/// (no FE config producer / anti-join — RT2-15).
/// </summary>
public enum AssignmentCombineMode
{
    Union = 0,
    Intersect = 1
}
