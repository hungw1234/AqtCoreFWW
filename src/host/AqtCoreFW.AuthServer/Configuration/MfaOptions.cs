namespace AqtCoreFW.AuthServer.Configuration;

/// <summary>
/// AuthServer MFA enforcement options (QĐ 742 1.6.b).
/// Bound from configuration section "Mfa".
/// </summary>
public sealed class MfaOptions
{
    public const string SectionName = "Mfa";

    /// <summary>
    /// When true, emit <c>mfa_enrolment_required=true</c> claim into access_token + id_token
    /// for users where mandatory MFA policy applies, grace period expired, and TOTP not yet enrolled.
    /// FE auth-provider reads the claim and redirects to <c>/account/mfa</c> enrolment wizard.
    /// Setting this to false reverts to the header-only fallback (<c>X-Mfa-Required</c>) which fires
    /// only when the user calls a <c>[RequiresMfa]</c> endpoint — UX nudge disabled but enforcement intact.
    /// Default: true.
    /// </summary>
    public bool RedirectOnEnrolmentRequired { get; init; } = true;
}
