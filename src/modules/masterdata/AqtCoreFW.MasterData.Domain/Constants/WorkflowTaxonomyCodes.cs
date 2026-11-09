namespace AqtCoreFW.MasterData.Domain.Constants;

/// <summary>
/// Canonical baseline codes for workflow state + action taxonomies (phase-00 §Seed Data).
/// Consumed by: seeder (INSERT-IF-NOT-EXISTS by Code, R2), phase-01 CaseProcess definition,
/// FE autocomplete fallback, validator regex reference.
/// Every code MUST match regex ^[A-Za-z][A-Za-z0-9_]*$ (guarded by unit test).
/// Case-insensitive uniqueness enforced by SQL Server default CI_AS collation (Q6 revised).
/// </summary>
public static class WorkflowTaxonomyCodes
{
    /// <summary>
    /// 10 baseline state codes. ReturnedForRevision added in phase-01 (naming-drift resolution
    /// option B, plan phase-01 line 38) to preserve parity with <c>CaseStatus.ReturnedForRevision</c>
    /// enum. Kept alongside generic Returned (R10) — Returned is the canonical taxonomy code
    /// for new definitions; ReturnedForRevision exists for the legacy Cases workflow only.
    /// </summary>
    public static class WorkflowStates
    {
        public const string Draft = "Draft";
        public const string Submitted = "Submitted";
        public const string UnderReview = "UnderReview";
        public const string Assigned = "Assigned";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Returned = "Returned";
        public const string ReturnedForRevision = "ReturnedForRevision";
        public const string Closed = "Closed";
        public const string Cancelled = "Cancelled";
    }

    /// <summary>9 baseline action codes — unchanged from round 1 plan.</summary>
    public static class WorkflowActions
    {
        public const string Submit = "Submit";
        public const string Assign = "Assign";
        public const string Approve = "Approve";
        public const string Reject = "Reject";
        public const string Return = "Return";
        public const string Close = "Close";
        public const string Cancel = "Cancel";
        public const string Reopen = "Reopen";
        public const string Reassign = "Reassign";
    }
}
