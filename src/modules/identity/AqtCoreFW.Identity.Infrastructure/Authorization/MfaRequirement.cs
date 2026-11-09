using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// Authorization requirement that enforces a recent MFA verification.
/// Evaluated by <see cref="MfaRequirementHandler"/>.
/// </summary>
public sealed class MfaRequirement(int maxAgeHours) : IAuthorizationRequirement
{
    /// <summary>Maximum allowed age (in hours) of the <c>mfa_at</c> claim.</summary>
    public int MaxAgeHours { get; } = maxAgeHours;
}
