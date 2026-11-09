using System.Security.Claims;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.Identity.Application.Authorization;

/// <summary>
/// Handles <see cref="AnyPermissionRequirement"/> by succeeding when the user's effective
/// permission summary contains AT LEAST ONE of the requirement's codes (OR semantics).
///
/// Mirrors <see cref="PermissionAuthorizationHandler"/> verbatim except for the Any-of check —
/// same Redis-cached <see cref="IEffectivePermissionService"/> summary, same implicit deny when
/// the user is unauthenticated or holds none of the codes.
/// </summary>
public sealed class AnyPermissionAuthorizationHandler
    : AuthorizationHandler<AnyPermissionRequirement>
{
    private readonly IEffectivePermissionService _permissionService;

    public AnyPermissionAuthorizationHandler(IEffectivePermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx,
        AnyPermissionRequirement requirement)
    {
        var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? ctx.User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return; // Deny implicitly — unauthenticated

        var summary = await _permissionService.GetSummaryAsync(userId);

        if (requirement.PermissionCodes.Any(code => summary.PermissionCodes.Contains(code)))
            ctx.Succeed(requirement);
    }
}
