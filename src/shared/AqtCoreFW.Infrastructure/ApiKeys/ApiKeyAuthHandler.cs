using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Infrastructure.ApiKeys;

/// <summary>
/// Authentication handler for M2M API key scheme.
/// Reads "X-Api-Key" header → validates via ApiKeyService (SHA-256 + Redis cache).
/// On success: injects client_id, tenantId, role=ServiceAccount, and scope claims.
/// </summary>
public sealed class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ApiKeyService apiKeyService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApiKeyQuery = "api_key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Try header first, then query param (for webhooks/callbacks)
        var rawKey = Request.Headers[ApiKeyHeader].FirstOrDefault()
            ?? Request.Query[ApiKeyQuery].FirstOrDefault();

        if (string.IsNullOrEmpty(rawKey))
            return AuthenticateResult.NoResult();

        var record = await apiKeyService.ValidateAsync(rawKey, Context.RequestAborted);
        if (record is null)
        {
            Logger.LogWarning("API key validation failed for prefix {Prefix}",
                rawKey.Length > 12 ? rawKey[4..12] : "?");
            return AuthenticateResult.Fail("Invalid or expired API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, record.Name),
            new("client_id", record.ClientId),
            new("tenant_id", record.TenantId.ToString()),
            new(ClaimTypes.Role, "ServiceAccount")
        };

        // Add scope claims — each scope becomes a separate claim
        foreach (var scope in record.Scopes)
            claims.Add(new Claim("scope", scope.ScopePermission));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = $"ApiKey realm=\"{Options.ClaimsIssuer}\"";
        return Task.CompletedTask;
    }
}
