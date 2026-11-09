using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// Marks a controller or action as requiring a recent MFA verification.
/// The policy is resolved dynamically by <see cref="MfaPolicyProvider"/> so no
/// up-front policy registration is needed in Program.cs.
///
/// Usage:
///   [RequiresMfa]            // default 8-hour window
///   [RequiresMfa(maxAgeHours: 1)]  // stricter — require fresh MFA within 1 hour
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresMfaAttribute : AuthorizeAttribute
{
    /// <summary>Policy name prefix used by <see cref="MfaPolicyProvider"/> for dynamic lookup.</summary>
    public const string PolicyPrefix = "RequiresMfa:";

    /// <param name="maxAgeHours">Maximum age (hours) of the mfa_at claim. Must be &gt; 0.</param>
    public RequiresMfaAttribute(int maxAgeHours = 8)
    {
        if (maxAgeHours <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAgeHours), "maxAgeHours must be greater than 0.");

        Policy = $"{PolicyPrefix}{maxAgeHours}";
    }
}
