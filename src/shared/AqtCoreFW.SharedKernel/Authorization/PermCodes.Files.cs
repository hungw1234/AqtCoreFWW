namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── FILES ─────────────────────────────────────────────────
    public static class Files
    {
        public static class File
        {
            public const string View = "FILES.FILE.VIEW";
            public const string Upload = "FILES.FILE.UPLOAD";
            public const string Download = "FILES.FILE.DOWNLOAD";
            public const string Delete = "FILES.FILE.DELETE";
        }
        public static class Template
        {
            public const string View = "FILES.TEMPLATE.VIEW";
            public const string Manage = "FILES.TEMPLATE.MANAGE";
        }
        public static class Retention
        {
            public const string View = "FILES.RETENTION.VIEW";
            public const string Manage = "FILES.RETENTION.MANAGE";
        }
    }
}
