namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── REPORT ────────────────────────────────────────────────
    public static class Report
    {
        public static class Dash
        {
            public const string View = "REPORT.DASH.VIEW";
            public const string Manage = "REPORT.DASH.MANAGE";
        }
        /// <summary>REPORT.REPORT resource — named ReportRes to avoid clash with parent class.</summary>
        public static class ReportRes
        {
            public const string View = "REPORT.REPORT.VIEW";
            public const string Export = "REPORT.REPORT.EXPORT";
            public const string Manage = "REPORT.REPORT.MANAGE";
        }
        public static class Widget
        {
            public const string Manage = "REPORT.WIDGET.MANAGE";
        }
    }
}
