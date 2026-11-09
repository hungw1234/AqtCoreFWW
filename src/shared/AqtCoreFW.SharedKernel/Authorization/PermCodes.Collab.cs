namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── COLLAB ────────────────────────────────────────────────
    public static class Collab
    {
        public static class Conv
        {
            public const string View = "COLLAB.CONV.VIEW";
            public const string Create = "COLLAB.CONV.CREATE";
        }
        public static class Msg
        {
            public const string Send = "COLLAB.MSG.SEND";
        }
    }
}
