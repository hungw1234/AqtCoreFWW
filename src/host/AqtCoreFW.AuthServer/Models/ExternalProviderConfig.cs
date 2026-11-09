namespace AqtCoreFW.AuthServer.Models;

/// <summary>
/// Configuration for an external OIDC identity provider, loaded from appsettings.
/// Phase 3 adds DB-backed config via JitProviderConfig entity.
/// </summary>
public sealed class ExternalProviderConfig
{
    /// <summary>ASP.NET auth scheme name — used as unique key.</summary>
    public string Scheme { get; set; } = string.Empty;

    /// <summary>Human-readable name shown on login page SSO button.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>OIDC issuer URL (e.g., https://sso.example.gov.vn).</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>OAuth2 client ID registered with the external IdP.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client secret (null for public clients).</summary>
    public string? ClientSecret { get; set; }
}
