namespace AqtCoreFW.MasterData.Domain.Enums;

/// <summary>
/// Semantic grouping for workflow state taxonomy rows.
/// Admin UI filters/colors states by category; runtime engine does NOT depend on this.
/// </summary>
public enum WorkflowStateCategory
{
    /// <summary>Entry states — where instances are born (e.g. Draft).</summary>
    Initial = 0,

    /// <summary>Work-in-progress states — transitional (e.g. Submitted, UnderReview).</summary>
    Intermediate = 1,

    /// <summary>Success terminal states — closed with positive outcome (e.g. Approved, Closed).</summary>
    Terminal = 2,

    /// <summary>Negative terminal states — instance ended without approval (e.g. Rejected, Cancelled).</summary>
    Rejected = 3,
}
