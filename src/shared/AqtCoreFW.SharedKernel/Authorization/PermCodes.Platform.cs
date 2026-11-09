namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── PLATFORM ──────────────────────────────────────────────
    /// <summary>
    /// Platform-admin tier — managed by SystemAdmin role only.
    /// First introduced phase-00 D6: taxonomy writes require platform-global authority,
    /// tenant-admins cannot mutate.
    /// </summary>
    public static class Platform
    {
        public static class WorkflowTaxonomy
        {
            public const string Manage = "PLATFORM.WORKFLOW_TAXONOMY.MANAGE";
        }
    }
}
