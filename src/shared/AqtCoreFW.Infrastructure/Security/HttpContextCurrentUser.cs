using System.Security.Claims;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;

namespace AqtCoreFW.Infrastructure.Security;

/// <summary>
/// Resolves the current user from the JWT claims in the active HTTP request.
/// Registered as scoped — one instance per request.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid UserId => Guid.TryParse(
        User?.FindFirstValue("sub") ?? User?.FindFirstValue(ClaimTypes.NameIdentifier),
        out var id) ? id : Guid.Empty;

    public Guid? TenantId => Guid.TryParse(
        User?.FindFirstValue("tenant_id"), out var tid) ? tid : null;

    public string UserName =>
        User?.FindFirstValue("name")
        ?? User?.FindFirstValue(ClaimTypes.Name)
        ?? User?.FindFirstValue("preferred_username")
        ?? User?.FindFirstValue(ClaimTypes.Email)
        ?? UserId.ToString();

    public string[] Roles => User?.Claims
        .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
        .Select(c => c.Value).ToArray() ?? [];

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
