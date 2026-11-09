using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using Microsoft.AspNetCore.Http;

namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// Test stub for <see cref="IEffectivePermissionService"/> bridging the legacy role-based
/// header semantics (<c>X-Test-Roles</c> in <see cref="TestAuthHandler"/>) to the
/// permission-code authorization model enforced post-D5/D6/D7.
///
/// Behavior (evaluated in order):
///   1. If <c>X-Test-Permissions</c> header is present, return exactly those permission codes.
///      This enables fine-grained RBAC tests (e.g., user with only View but not Manage).
///   2. If the current request carries role <c>Admin</c> or <c>SystemAdmin</c>, returns a
///      summary whose <c>PermissionCodes</c> = <see cref="PermCodes.All"/> (the full compile-time
///      permission universe, 122 codes across 19 modules).
///   3. Otherwise, returns an empty set â†’ <c>PermissionAuthorizationHandler</c> denies â†’ 403.
///
/// This mirrors pre-cluster test assumptions (Admin role â‡’ full access, other roles â‡’ deny
/// for admin-gated endpoints) without relying on the <c>E2ETestDataSeeder</c> (Development-only,
/// skipped under <c>ASPNETCORE_ENVIRONMENT=Testing</c>) and without an <c>AlwaysTrueSet</c> that
/// would diverge from production semantics under enumeration or serialization.
///
/// The backing <see cref="HashSet{T}"/> is real â€” <c>Contains</c>, <c>GetEnumerator</c>,
/// <c>Count</c>, and JSON serialization all behave identically to production.
/// </summary>
public sealed class TestEffectivePermissionService : IEffectivePermissionService
{
    private static readonly string[] AdminRoleCodes = ["Admin", "SystemAdmin"];

    private static readonly HashSet<string> AllKnownPermCodes =
        PermCodes.All.ToHashSet();

    private static readonly HashSet<string> EmptyPermCodes = [];

    private readonly IHttpContextAccessor _httpCtx;

    public TestEffectivePermissionService(IHttpContextAccessor httpCtx)
    {
        _httpCtx = httpCtx;
    }

    public Task<EffectivePermissionSummary> GetSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        var perms = ResolvePermissionCodes();
        return Task.FromResult(new EffectivePermissionSummary
        {
            UserId = userId,
            PermissionCodes = perms
        });
    }

    public Task InvalidateAsync(Guid userId) => Task.CompletedTask;

    /// <summary>
    /// Resolves permission codes from request headers.
    /// Priority: explicit X-Test-Permissions > role-based (Admin/SystemAdmin â†’ all) > empty.
    /// </summary>
    private HashSet<string> ResolvePermissionCodes()
    {
        // Priority 1: Explicit permission codes header for RBAC tests
        var permHeader = _httpCtx.HttpContext?.Request.Headers[TestAuthHandler.PermissionsHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(permHeader))
        {
            var codes = permHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return codes.ToHashSet();
        }

        // Priority 2: Admin/SystemAdmin roles â†’ all permissions
        if (RequestHasAdminRole())
            return AllKnownPermCodes;

        // Priority 3: No permissions
        return EmptyPermCodes;
    }

    private bool RequestHasAdminRole()
    {
        var header = _httpCtx.HttpContext?.Request.Headers[TestAuthHandler.RolesHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header)) return false;

        var roles = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return roles.Any(r => AdminRoleCodes.Contains(r, StringComparer.Ordinal));
    }
}
