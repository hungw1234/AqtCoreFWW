namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── AUDIT ─────────────────────────────────────────────────
    public static class Audit
    {
        public static class Log
        {
            public const string View = "AUDIT.LOG.VIEW";
            public const string Export = "AUDIT.LOG.EXPORT";
        }
        public static class Login
        {
            public const string View = "AUDIT.LOGIN.VIEW";
        }
        public static class Rtbf
        {
            public const string View = "AUDIT.RTBF.VIEW";
            public const string Execute = "AUDIT.RTBF.EXECUTE";
        }
        public static class Incident
        {
            public const string View = "AUDIT.INCIDENT.VIEW";
            public const string Manage = "AUDIT.INCIDENT.MANAGE";
        }
        /// <summary>QĐ 742 §3.2.b — audit log retention windows (per-tenant + per-category). Phase-01 Slice D2.</summary>
        public static class Retention
        {
            public const string View = "AUDIT.RETENTION.VIEW";
            public const string Manage = "AUDIT.RETENTION.MANAGE";
        }
    }
}
