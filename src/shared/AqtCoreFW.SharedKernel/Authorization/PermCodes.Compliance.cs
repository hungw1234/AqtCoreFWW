namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── COMPLIANCE ────────────────────────────────────────────
    /// <summary>
    /// QĐ 742 cấp 4 §4.4 — data classification inventory + handling-rule admin (phase-04 Wave 2).
    /// View: read-only inventory scan + rule list.
    /// Manage: update per-level handling rules (SystemAdmin only).
    /// </summary>
    public static class Compliance
    {
        public const string View = "COMPLIANCE.CLASSIFICATION.VIEW";
        public const string Manage = "COMPLIANCE.CLASSIFICATION.MANAGE";
    }
}
