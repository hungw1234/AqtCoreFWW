namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── MASTER ────────────────────────────────────────────────
    public static class Master
    {
        public static class Data
        {
            public const string View = "MASTER.DATA.VIEW";
            public const string Manage = "MASTER.DATA.MANAGE";
        }
        public static class Dict
        {
            public const string View = "MASTER.DICT.VIEW";
            public const string Manage = "MASTER.DICT.MANAGE";
        }
        public static class ExtMap
        {
            public const string Manage = "MASTER.EXTMAP.MANAGE";
        }
        /// <summary>Phase-00: workflow state/action taxonomy vocabulary (global masterdata).</summary>
        public static class WorkflowTaxonomy
        {
            public const string View = "MASTER.WORKFLOW_TAXONOMY.VIEW";
        }
    }
}
