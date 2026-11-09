namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── AI ────────────────────────────────────────────────────
    public static class Ai
    {
        public static class Extract
        {
            public const string Use = "AI.EXTRACT.USE";
        }
        public static class Model
        {
            public const string View = "AI.MODEL.VIEW";
            public const string Manage = "AI.MODEL.MANAGE";
        }
        public static class Prompt
        {
            public const string View = "AI.PROMPT.VIEW";
            public const string Manage = "AI.PROMPT.MANAGE";
        }
    }
}
