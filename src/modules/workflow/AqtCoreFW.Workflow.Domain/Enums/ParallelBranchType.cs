namespace AqtCoreFW.Workflow.Domain.Enums;

/// <summary>
/// Join strategy for parallel branches.
/// And = all must approve, Or = first resolution wins, NOfM = at least RequiredCount approvals.
/// </summary>
public enum ParallelBranchType
{
    And = 0,
    Or = 1,
    NOfM = 2
}
