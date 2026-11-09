using System.Security.Claims;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;

namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// ICurrentUser implementation for tests — reads from ClaimsPrincipal injected by TestAuthHandler.
/// </summary>
public class TestCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid UserId => Guid.TryParse(
        User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public Guid? TenantId => Guid.TryParse(
        User?.FindFirstValue("tenant_id"), out var tid) ? tid : null;

    public string UserName =>
        User?.FindFirstValue(ClaimTypes.Name) ?? "TestUser";

    public string[] Roles => User?.Claims
        .Where(c => c.Type == ClaimTypes.Role)
        .Select(c => c.Value).ToArray() ?? [];

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
