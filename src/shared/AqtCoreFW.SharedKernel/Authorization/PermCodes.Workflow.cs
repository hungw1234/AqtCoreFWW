namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── WORKFLOW ──────────────────────────────────────────────
    public static class Workflow
    {
        public static class Def
        {
            public const string View = "WORKFLOW.DEF.VIEW";
            public const string Manage = "WORKFLOW.DEF.MANAGE";
        }
        public static class Task
        {
            public const string View = "WORKFLOW.TASK.VIEW";
            public const string Process = "WORKFLOW.TASK.PROCESS";
        }
        public static class Instance
        {
            public const string View = "WORKFLOW.INSTANCE.VIEW";
            /// <summary>Phase-04-A follow-up 2026-05-22 — 4-A-A invariant closure: Create instance requires Manage (was naked [Authorize]).</summary>
            public const string Manage = "WORKFLOW.INSTANCE.MANAGE";
        }
        public static class Assign
        {
            public const string Manage = "WORKFLOW.ASSIGN.MANAGE";
        }
        public static class Delegation
        {
            public const string Manage = "WORKFLOW.DELEGATION.MANAGE";
        }
        public static class Analytics
        {
            public const string View = "WORKFLOW.ANALYTICS.VIEW";
        }
        /// <summary>4-eyes GOV: CREATE and APPROVE are distinct permissions.</summary>
        public static class Migration
        {
            public const string Read    = "WORKFLOW.MIGRATION.READ";
            public const string Create  = "WORKFLOW.MIGRATION.CREATE";
            public const string Approve = "WORKFLOW.MIGRATION.APPROVE";
            public const string Execute = "WORKFLOW.MIGRATION.EXECUTE";
            public const string Cancel  = "WORKFLOW.MIGRATION.CANCEL";
        }
    }
}
