using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.AuthServer.Services;

/// <summary>
/// QĐ 742 1.6.b — predicate for "user must enrol MFA before continuing".
/// Identical compliance semantics to ROPC Branch B; extracted as an injectable service to keep
/// AuthorizationController thin and the predicate unit-testable in isolation.
/// </summary>
public interface IMfaEnrolmentEvaluator
{
    /// <summary>
    /// Returns true iff: user has no TOTP enrolment, a mandatory MFA policy applies via role
    /// resolution, and the grace deadline (MfaGraceUntil ?? CreatedAtUtc + GraceDays) has passed.
    /// Pure predicate — does NOT consult feature flags. Callers gate by config separately so that
    /// ROPC's hard-block path (compliance) remains independent of FE-redirect feature flag.
    /// </summary>
    Task<bool> RequiresEnrolmentAsync(ApplicationUser user, CancellationToken ct = default);
}

public sealed class MfaEnrolmentEvaluator(IMfaPolicyResolver resolver) : IMfaEnrolmentEvaluator
{
    public async Task<bool> RequiresEnrolmentAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (user.TotpEnrolledAt.HasValue) return false;

        var policy = await resolver.ResolveAsync(user.Id, ct);
        if (policy is not { IsMandatory: true }) return false;

        var graceDeadline = user.MfaGraceUntil ?? user.CreatedAtUtc.AddDays(policy.GraceDays);
        return DateTime.UtcNow > graceDeadline;
    }
}
