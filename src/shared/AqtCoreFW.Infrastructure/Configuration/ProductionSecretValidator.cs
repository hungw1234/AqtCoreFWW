using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Startup gate that prevents production hosts from starting with missing or dev-placeholder secrets.
/// Enforces fail-fast pattern (QĐ 742 §4.4.a "không hardcode credential").
///
/// Placement: call IMMEDIATELY after <c>app.Build()</c>, BEFORE MigrateAsync or app.Run().
/// Rationale: catching a missing ConnectionString before MigrateAsync gives a clear error message
/// instead of a SQL connection exception buried in migration stack traces.
/// </summary>
public static class ProductionSecretValidator
{
    // Known dev-only placeholder values that must never appear in production.
    // AAAA... is the default FieldKey fallback from appsettings.Development.json.
    private static readonly HashSet<string> KnownDevMarkers = new(StringComparer.Ordinal)
    {
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        "DEV_PLACEHOLDER",
        AuthResourceClient.DevOnlySecret,
    };

    /// <summary>Required secret keys validated in both Api and AuthServer hosts.</summary>
    private static readonly string[] RequiredKeys =
    [
        "ConnectionStrings:Default",
        "Encryption:FieldKey",
        "Audit:HmacKey",
        AuthResourceClient.SecretKey,
    ];

    /// <summary>
    /// Validates that production secrets are properly configured.
    /// No-op when <see cref="IHostEnvironment.IsProduction()"/> returns false.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any required key is null/empty, starts with <c>DEV_</c>,
    /// or equals a known dev placeholder marker.
    /// </exception>
    public static IHost ValidateProductionSecrets(this IHost host)
    {
        var env = host.Services.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        if (env is null || !env.IsProduction())
            return host;

        var config = host.Services.GetService(typeof(IConfiguration)) as IConfiguration
            ?? throw new InvalidOperationException("IConfiguration not available in host services.");

        foreach (var key in RequiredKeys)
        {
            var value = config[key];
            AssertSecretValid(key, value);
        }

        return host;
    }

    /// <summary>
    /// Validates a single secret value against the production rules.
    /// Public for unit-test access without IHost wiring.
    /// </summary>
    public static void AssertSecretValid(string key, string? value)
    {
        // IsNullOrWhiteSpace catches typos like "  " in config (M4 from S4 review).
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Production secret missing or contains DEV marker: {key}");

        if (value.StartsWith("DEV_", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Production secret missing or contains DEV marker: {key}");

        if (KnownDevMarkers.Contains(value))
            throw new InvalidOperationException(
                $"Production secret missing or contains DEV marker: {key}");
    }
}
