using System.Security.Claims;

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// Shared evaluation of the <c>mfa_at</c> claim recency.
///
/// Used by two collaborators that must agree on the exact same notion of "MFA is
/// fresh enough":
///   - <see cref="MfaRequirementHandler"/> — grants or denies the requirement.
///   - <see cref="MfaForbidResultHandler"/> — decides whether a 403 is genuinely
///     caused by an unmet MFA step-up, so the <c>X-Mfa-Required</c> hint is emitted
///     ONLY when MFA is the actual missing condition.
///
/// The result handler cannot rely on <c>AuthorizationFailure.FailedRequirements</c>:
/// the requirement handler signals an unmet step-up via <c>context.Fail()</c>, which
/// collapses the aggregate into an explicit failure with an EMPTY failed-requirements
/// set. Re-evaluating the claim here is the only reliable way to tell an MFA-caused
/// 403 apart from a permission-caused 403 on an endpoint that merely also carries
/// <c>[RequiresMfa]</c>.
/// </summary>
public static class MfaClaimEvaluator
{
    /// <summary>Claim type carrying the Unix epoch (seconds) of the last MFA verification.</summary>
    public const string MfaAtClaimType = "mfa_at";

    /// <summary>
    /// True when <paramref name="user"/> carries a parseable <c>mfa_at</c> whose age does
    /// not exceed <paramref name="maxAgeHours"/>. Absent, malformed, or stale → false.
    /// </summary>
    public static bool IsMfaRecent(ClaimsPrincipal? user, int maxAgeHours)
    {
        var claim = user?.FindFirst(MfaAtClaimType);
        if (claim is null)
        {
            return false;
        }

        if (!long.TryParse(claim.Value, out var epochSeconds))
        {
            return false;
        }

        var mfaAt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime;
        return (DateTime.UtcNow - mfaAt).TotalHours <= maxAgeHours;
    }
}
