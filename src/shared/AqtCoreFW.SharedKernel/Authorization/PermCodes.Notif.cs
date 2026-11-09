namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── NOTIF ─────────────────────────────────────────────────
    public static class Notif
    {
        public static class Msg
        {
            public const string View = "NOTIF.MSG.VIEW";
            public const string Send = "NOTIF.MSG.SEND";
        }
        public static class Template
        {
            public const string View = "NOTIF.TEMPLATE.VIEW";
            public const string Manage = "NOTIF.TEMPLATE.MANAGE";
        }
    }
}
