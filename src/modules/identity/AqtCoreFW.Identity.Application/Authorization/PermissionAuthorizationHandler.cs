using System.Security.Claims;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.Identity.Application.Authorization;

/// <summary>
/// Handles <see cref="PermissionRequirement"/> by checking the user's effective
/// permission codes via <see cref="IEffectivePermissionService"/>.
///
/// Fast path: if the permission code is present in the cached summary, succeed immediately.
/// The service caches per-user in Redis (TTL 10 min) so DB is rarely hit.
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    private readonly IEffectivePermissionService _permissionService;

    public PermissionAuthorizationHandler(IEffectivePermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx,
        PermissionRequirement requirement)
    {
        var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? ctx.User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return; // Deny implicitly — unauthenticated

        var summary = await _permissionService.GetSummaryAsync(userId);

        if (summary.PermissionCodes.Contains(requirement.PermissionCode))
            ctx.Succeed(requirement);
    }
}
