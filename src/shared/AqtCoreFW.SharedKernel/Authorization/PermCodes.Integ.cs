namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── INTEG ─────────────────────────────────────────────────
    public static class Integ
    {
        public static class Contract
        {
            public const string View = "INTEG.CONTRACT.VIEW";
            public const string Manage = "INTEG.CONTRACT.MANAGE";
        }
        public static class Partner
        {
            public const string View = "INTEG.PARTNER.VIEW";
            public const string Manage = "INTEG.PARTNER.MANAGE";
        }
        public static class MsgLog
        {
            public const string View = "INTEG.MSGLOG.VIEW";
        }
    }
}
