namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Configuration for short-lived doc-flow JWTs (W3, QĐ 742 mục 5.1).
///
/// Bind via <c>services.Configure&lt;DocFlowJwtOptions&gt;(config.GetSection("Signing:DocFlowJwt"))</c>.
///
/// SigningKey: HS256 HMAC key (≥32 bytes recommended). In production, sourced from Vault at
///   path <c>aqt/signing/docflow-jwt/key</c> via <see cref="IKeyResolver"/>; in development,
///   set in appsettings.Development.json (dev placeholder value only — never commit a real key).
///
/// IssuerName: JWT iss claim value (default "AqtCoreFW").
/// TtlSeconds: JWT lifetime in seconds (default 300 = 5 min). The nonce ExpiresAt is set to
///   the same TTL so both expire together.
/// </summary>
public sealed class DocFlowJwtOptions
{
    /// <summary>HS256 signing key — must be at least 32 characters. Sourced from Vault in production.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Value of the <c>iss</c> JWT claim.</summary>
    public string IssuerName { get; set; } = "AqtCoreFW";

    /// <summary>Token lifetime in seconds. Default 300 (5 minutes).</summary>
    public int TtlSeconds { get; set; } = 300;
}
