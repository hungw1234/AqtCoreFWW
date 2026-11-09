namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── SEARCH ────────────────────────────────────────────────
    public static class Search
    {
        public static class Query
        {
            public const string Use = "SEARCH.QUERY.USE";
        }
        public static class Saved
        {
            public const string Manage = "SEARCH.SAVED.MANAGE";
        }
    }
}
