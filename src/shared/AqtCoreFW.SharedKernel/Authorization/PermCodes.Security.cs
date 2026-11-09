namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── SECURITY ─────────────────────────────────────────────
    /// <summary>
    /// QĐ 742 1.6.a — MFA policy admin (4 codes, phase-03).
    /// MfaPolicy.View/Manage: per-role policy CRUD.
    /// MfaEmergency.Request/Approve: 2-admin approval flow for emergency disable.
    /// </summary>
    public static class Security
    {
        public static class MfaPolicy
        {
            public const string View = "SECURITY.MFA_POLICY.VIEW";
            public const string Manage = "SECURITY.MFA_POLICY.MANAGE";
        }
        public static class MfaEmergency
        {
            public const string Request = "SECURITY.MFA_EMERGENCY.REQUEST";
            public const string Approve = "SECURITY.MFA_EMERGENCY.APPROVE";
        }

        /// <summary>QĐ 742 §2.2.a — IP filter rule admin (phase-05, +2 codes).</summary>
        public static class IpRule
        {
            public const string View   = "SECURITY.IP_RULE.VIEW";
            public const string Manage = "SECURITY.IP_RULE.MANAGE";
        }

        /// <summary>QĐ 742 D7 — per-criterion compliance visibility pages (phase-05 Wave-2, +1 code).</summary>
        public static class Health
        {
            public const string View = "SECURITY.HEALTH.VIEW";
        }

        /// <summary>QĐ 742 §5.1 — digital signature provider config admin (phase-05b W6, +2 codes).</summary>
        public static class SignatureConfig
        {
            public const string View   = "SECURITY.SIGNATURE_CONFIG.VIEW";
            public const string Manage = "SECURITY.SIGNATURE_CONFIG.MANAGE";
        }
    }
}
