namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── SIGNATURE ─────────────────────────────────────────────
    /// <summary>
    /// Digital-signature self-service administration. Distinct from SECURITY.SIGNATURE_CONFIG
    /// (provider/algorithm config) — this gates the per-tenant queue where an admin approves or
    /// rejects users' self-service signing-certificate enrollments, so it can be granted without
    /// the far broader SYSTEM.CONFIG.MANAGE the queue previously reused.
    /// </summary>
    public static class Signature
    {
        public static class Enrollment
        {
            public const string Approve = "SIGNATURE.ENROLLMENT.APPROVE";
        }
    }
}
