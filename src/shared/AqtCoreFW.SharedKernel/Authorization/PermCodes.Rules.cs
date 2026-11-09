namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── RULES ─────────────────────────────────────────────────
    public static class Rules
    {
        public static class RuleSet
        {
            public const string View = "RULES.RULESET.VIEW";
            public const string Manage = "RULES.RULESET.MANAGE";
        }
        public static class DTable
        {
            public const string View = "RULES.DTABLE.VIEW";
            public const string Manage = "RULES.DTABLE.MANAGE";
        }
        public static class Eval
        {
            public const string Use = "RULES.EVAL.USE";
        }
    }
}
