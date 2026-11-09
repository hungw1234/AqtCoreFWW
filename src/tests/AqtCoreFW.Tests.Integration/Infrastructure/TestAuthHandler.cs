using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Tests.Integration.Infrastructure;

/// <summary>
/// Bypasses OpenIddict validation — injects ClaimsPrincipal directly from test headers.
/// Only active when ASPNETCORE_ENVIRONMENT=Testing.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string UserIdHeader = "X-Test-UserId";
    public const string RolesHeader = "X-Test-Roles";
    public const string TenantIdHeader = "X-Test-TenantId";
    /// <summary>
    /// Comma-separated permission codes for fine-grained RBAC tests.
    /// When present, TestEffectivePermissionService uses these codes instead of role-based logic.
    /// </summary>
    public const string PermissionsHeader = "X-Test-Permissions";
    /// <summary>
    /// When set to "false", suppresses the default fresh mfa_at claim — for tests that
    /// assert MfaRequirement-driven 403 step-up behavior. Default: claim is added.
    /// </summary>
    public const string MfaFreshHeader = "X-Test-MfaFresh";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Only authenticate if the test identity header is present.
        // Requests without X-Test-UserId are treated as unauthenticated → 401 via challenge.
        if (!Request.Headers.ContainsKey(UserIdHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = Request.Headers[UserIdHeader].FirstOrDefault()!;
        var roles = Request.Headers[RolesHeader].FirstOrDefault()?.Split(',') ?? [];
        var tenantId = Request.Headers[TenantIdHeader].FirstOrDefault();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Email, "test@test.vn"),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (tenantId != null) claims.Add(new("tenant_id", tenantId));

        // Phase-03 MFA: many admin endpoints carry [RequiresMfa] which is satisfied by a recent
        // mfa_at claim. Default to "fresh" so existing tests keep working; opt out with header.
        var mfaFresh = Request.Headers[MfaFreshHeader].FirstOrDefault();
        if (!string.Equals(mfaFresh, "false", StringComparison.OrdinalIgnoreCase))
            claims.Add(new("mfa_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
