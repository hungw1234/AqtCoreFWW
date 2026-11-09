namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── SYSPARAM ──────────────────────────────────────────────
    public static class SysParam
    {
        public static class Param
        {
            public const string View = "SYSPARAM.PARAM.VIEW";
            public const string Manage = "SYSPARAM.PARAM.MANAGE";
        }
        public static class Flag
        {
            public const string View = "SYSPARAM.FLAG.VIEW";
            public const string Manage = "SYSPARAM.FLAG.MANAGE";
        }
        public static class Announce
        {
            public const string View = "SYSPARAM.ANNOUNCE.VIEW";
            public const string Manage = "SYSPARAM.ANNOUNCE.MANAGE";
        }
    }
}
