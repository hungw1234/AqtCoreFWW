using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
// IAuthorizationMiddlewareResultHandler lives in Microsoft.AspNetCore.Authorization;
// the concrete AuthorizationMiddlewareResultHandler + PolicyAuthorizationResult live in .Policy.

namespace AqtCoreFW.Identity.Infrastructure.Authorization;

/// <summary>
/// Tags 403 responses produced by a failing <see cref="MfaRequirement"/> with
/// <c>X-Mfa-Required: true</c> so the SPA can distinguish a missing/stale MFA
/// session (redirect to enrolment / step-up) from a regular permission deny
/// (show the generic forbidden toast).
///
/// The endpoint's combined policy may carry BOTH an <see cref="MfaRequirement"/> and
/// an unrelated permission requirement. A 403 there can be caused by either. Emitting
/// the hint on mere presence of an <see cref="MfaRequirement"/> would mislabel a
/// permission deny as an MFA step-up — bouncing a user who simply lacks the permission
/// to the 2FA page. So the hint is emitted ONLY when the user's MFA is actually not
/// recent enough, re-evaluated via <see cref="MfaClaimEvaluator"/> (the failed-requirements
/// set is unreliable here: the requirement handler uses <c>context.Fail()</c>, which
/// yields an explicit failure with an empty failed-requirements collection).
///
/// Delegates the actual response writing to the framework's default
/// <see cref="AuthorizationMiddlewareResultHandler"/>.
/// </summary>
public sealed class MfaForbidResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string MfaRequiredHeader = "X-Mfa-Required";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        // Only annotate when (a) the request is forbidden, (b) the user is authenticated
        // (so this is not a 401-as-403), and (c) MFA step-up is the ACTUAL unmet
        // condition — an MfaRequirement is present AND the user's mfa_at is not recent
        // enough for it. A permission-caused 403 on an [RequiresMfa] endpoint whose MFA
        // is satisfied must surface as a plain forbidden, not a 2FA redirect.
        if (authorizeResult.Forbidden &&
            context.User?.Identity?.IsAuthenticated == true &&
            !context.Response.Headers.ContainsKey(MfaRequiredHeader) &&
            policy.Requirements.OfType<MfaRequirement>()
                .Any(req => !MfaClaimEvaluator.IsMfaRecent(context.User, req.MaxAgeHours)))
        {
            context.Response.Headers.Append(MfaRequiredHeader, "true");
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
