namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── ORG ───────────────────────────────────────────────────
    public static class Org
    {
        public static class Unit
        {
            public const string View = "ORG.UNIT.VIEW";
            public const string Manage = "ORG.UNIT.MANAGE";
        }
        public static class Staff
        {
            public const string View = "ORG.STAFF.VIEW";
            public const string Manage = "ORG.STAFF.MANAGE";
        }
    }
}
