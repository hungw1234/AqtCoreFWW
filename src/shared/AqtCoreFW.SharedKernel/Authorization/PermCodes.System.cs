namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── SYSTEM ────────────────────────────────────────────────
    public static class System
    {
        public static class Config
        {
            public const string View = "SYSTEM.CONFIG.VIEW";
            public const string Manage = "SYSTEM.CONFIG.MANAGE";
        }
    }
}
