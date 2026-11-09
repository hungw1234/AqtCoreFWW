namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── FORMS ─────────────────────────────────────────────────
    public static class Forms
    {
        public static class Template
        {
            public const string View = "FORMS.TEMPLATE.VIEW";
            public const string Manage = "FORMS.TEMPLATE.MANAGE";
        }
        public static class Submit
        {
            public const string View = "FORMS.SUBMIT.VIEW";
            public const string Create = "FORMS.SUBMIT.CREATE";
            public const string Approve = "FORMS.SUBMIT.APPROVE";
            public const string Reject = "FORMS.SUBMIT.REJECT";
            public const string Export = "FORMS.SUBMIT.EXPORT";
        }
        /// <summary>FORMS.VIEW resource — named FormView to avoid clash with C# keyword.</summary>
        public static class FormView
        {
            public const string Manage = "FORMS.VIEW.MANAGE";
        }

        /// <summary>
        /// Admin manual-override on orphan workflow-link retry rows (P6-DEFER-1 phase 05).
        /// Least-privilege — deliberately NOT a reuse of Submit.Approve (LOCKED Q3).
        /// Gates GET list + POST retry + POST shelve on WorkflowLinkPendingController.
        /// </summary>
        public static class WorkflowLink
        {
            public const string Manage = "FORMS.WFLINK.MANAGE";
        }
    }
}
