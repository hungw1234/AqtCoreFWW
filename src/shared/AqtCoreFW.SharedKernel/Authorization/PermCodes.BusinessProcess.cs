namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── BUSINESSPROCESS ───────────────────────────────────────
    /// <summary>
    /// BP MVP P4 — BusinessProcess definition + run lifecycle admin (+3 codes).
    /// View: list/read BP definitions + start BP Run (runtime).
    /// Manage: create/update BP definitions including form mappings.
    /// Activate: flip IsActive on/off (separated from Manage for 4-eyes governance).
    /// </summary>
    public static class BusinessProcess
    {
        public static class Bp
        {
            public const string View     = "BUSINESSPROCESS.BP.VIEW";
            public const string Manage   = "BUSINESSPROCESS.BP.MANAGE";
            public const string Activate = "BUSINESSPROCESS.BP.ACTIVATE";
        }
    }
}
