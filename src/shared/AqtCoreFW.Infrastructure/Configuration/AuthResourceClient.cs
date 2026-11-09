using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Single source for the confidential client the Api uses to call AuthServer introspection.
/// AuthServer seeds the client with this secret; the Api presents it on every introspection call.
/// The built-in secret is a well-known development value (it is in the repository), so it is only
/// accepted in <see cref="DevFallbackEnvironments"/>; every other environment must configure
/// <see cref="SecretKey"/> or the host fails at startup.
/// </summary>
public static class AuthResourceClient
{
    public const string ClientIdKey = "Auth:ResourceClientId";
    public const string SecretKey = "Auth:ResourceClientSecret";
    public const string DefaultClientId = "aqtcorefw-api-resource";

    /// <summary>Development-only secret. Rejected by <see cref="ProductionSecretValidator"/>.</summary>
    internal const string DevOnlySecret = "dev-resource-secret-change-in-prod";

    /// <summary>Environments allowed to fall back to <see cref="DevOnlySecret"/>; same set as the VNeID connector guard.</summary>
    public static readonly IReadOnlyList<string> DevFallbackEnvironments = ["Development", "Testing", "Benchmark"];

    public static string ResolveClientId(IConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration[ClientIdKey]) ? DefaultClientId : configuration[ClientIdKey]!;

    /// <summary>
    /// Returns the configured secret; outside <see cref="DevFallbackEnvironments"/> a missing value or the
    /// development value throws instead of silently using a secret that is public in the repository.
    /// </summary>
    public static string ResolveSecret(IConfiguration configuration, string environmentName)
    {
        var configured = configuration[SecretKey];
        var allowDevFallback = DevFallbackEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(configured))
        {
            if (allowDevFallback)
                return DevOnlySecret;
            throw new InvalidOperationException(
                $"{SecretKey} is required in environment '{environmentName}'. Set it from the secret store.");
        }

        if (!allowDevFallback && string.Equals(configured.Trim(), DevOnlySecret, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{SecretKey} uses the development value in environment '{environmentName}'. Rotate it.");

        return configured;
    }
}
