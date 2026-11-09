using System.Security.Claims;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;

namespace AqtCoreFW.Infrastructure.Security;

/// <summary>
/// Resolves the current tenant from JWT claims in the active HTTP request.
/// Registered as scoped — one instance per request.
/// </summary>
public sealed class HttpContextTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid? TenantId => Guid.TryParse(
        User?.FindFirstValue("tenant_id"), out var tid) ? tid : null;

    // SystemAdmin is the sole role that bypasses tenant isolation (cross-tenant escape).
    // Use the shared Roles constant so the cross-tenant gate stays in lockstep with role
    // checks elsewhere (e.g. role-grant guards) and cannot drift on a typo.
    public bool IsSystemAdmin =>
        User?.IsInRole(Roles.SystemAdmin) ?? false;
}
