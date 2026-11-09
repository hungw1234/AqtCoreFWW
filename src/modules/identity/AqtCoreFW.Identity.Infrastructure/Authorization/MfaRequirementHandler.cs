using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// Evaluates <see cref="MfaRequirement"/> against the current user's <c>mfa_at</c> claim.
/// The claim is expected to carry a Unix epoch (seconds) timestamp set by the OpenIddict
/// token service at the time of MFA verification.
///
/// Fail conditions:
///   - <c>mfa_at</c> claim is absent.
///   - Claim value cannot be parsed as a long integer.
///   - Time elapsed since MFA verification exceeds <see cref="MfaRequirement.MaxAgeHours"/>.
/// </summary>
public sealed class MfaRequirementHandler : AuthorizationHandler<MfaRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MfaRequirement requirement)
    {
        // Recency check lives in the shared evaluator so the result handler can
        // re-derive the same verdict when deciding whether to emit X-Mfa-Required.
        if (MfaClaimEvaluator.IsMfaRecent(context.User, requirement.MaxAgeHours))
        {
            context.Succeed(requirement);
        }
        else
        {
            // Absent, malformed, or stale mfa_at — require fresh step-up.
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
