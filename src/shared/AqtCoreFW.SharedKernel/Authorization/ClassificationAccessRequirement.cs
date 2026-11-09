using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>Requires user ClearanceLevel ≥ resource ClassificationLevel.</summary>
public sealed class ClassificationAccessRequirement : IAuthorizationRequirement
{
    public ClassificationLevel MinimumLevel { get; }

    public ClassificationAccessRequirement(ClassificationLevel minimumLevel = ClassificationLevel.Internal)
        => MinimumLevel = minimumLevel;
}

/// <summary>Checks user's clearance claim against required classification level.</summary>
public sealed class ClassificationAccessHandler
    : AuthorizationHandler<ClassificationAccessRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx,
        ClassificationAccessRequirement requirement)
    {
        // Admin bypasses classification check
        if (ctx.User.IsInRole(Roles.Admin) || ctx.User.IsInRole(Roles.SystemAdmin))
        {
            ctx.Succeed(requirement);
            return Task.CompletedTask;
        }

        // C5/F-15: When acting under a delegation, use the capped delegated_clearance_level
        // (set by ClaimsEnrichmentTransformer to Min(delegate, delegator) clearance).
        // Falls back to the user's own clearance_level for non-delegated requests.
        var clearanceClaim = ctx.User.FindFirst("delegated_clearance_level")?.Value
            ?? ctx.User.FindFirst("clearance_level")?.Value;
        if (Enum.TryParse<ClassificationLevel>(clearanceClaim, out var userLevel)
            && userLevel >= requirement.MinimumLevel)
        {
            ctx.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
