using AqtCoreFW.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// Dynamic authorization policy provider that resolves <c>RequiresMfa:{hours}</c> policies
/// at runtime without requiring explicit up-front registration in Program.cs.
///
/// Pattern: intercept policy names that start with <see cref="RequiresMfaAttribute.PolicyPrefix"/>,
/// parse the trailing integer as max-age hours, build an <see cref="AuthorizationPolicy"/>
/// with a single <see cref="MfaRequirement"/>, and fall back to the default provider for
/// all other policy names.
///
/// Registration (Phase C): replace the default IAuthorizationPolicyProvider in DI:
///   services.AddSingleton&lt;IAuthorizationPolicyProvider, MfaPolicyProvider&gt;();
/// </summary>
public sealed class MfaPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequiresMfaAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var suffix = policyName[RequiresMfaAttribute.PolicyPrefix.Length..];

            if (int.TryParse(suffix, out var maxAgeHours) && maxAgeHours > 0)
            {
                return new AuthorizationPolicyBuilder()
                    .AddRequirements(new MfaRequirement(maxAgeHours))
                    .Build();
            }
        }

        // Delegate all non-MFA policies to the standard provider.
        return await base.GetPolicyAsync(policyName);
    }
}
