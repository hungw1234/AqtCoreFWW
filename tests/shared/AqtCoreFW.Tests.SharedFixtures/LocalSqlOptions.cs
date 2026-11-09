namespace AqtCoreFW.Tests.SharedFixtures;

/// <summary>
/// Configuration options for local SQL Server integration test execution.
/// Read once from environment variables at process start and cached for the session.
/// This ensures consistent configuration across all test fixtures in a single run.
/// </summary>
public sealed class LocalSqlOptions
{
    private static LocalSqlOptions? _cached;
    private static readonly object _lock = new();

    /// <summary>Whether local SQL Server mode is enabled (vs Testcontainers).</summary>
    public bool IsEnabled { get; init; }

    /// <summary>SQL Server hostname:port. Default: "localhost,1433".</summary>
    public string Server { get; init; } = "localhost,1433";

    /// <summary>SA password for SQL authentication.</summary>
    public string SaPassword { get; init; } = string.Empty;

    /// <summary>Prefix for test database names. Default: "AqtCoreFW_Test".</summary>
    public string DbPrefix { get; init; } = "AqtCoreFW_Test";

    /// <summary>
    /// Reads configuration from environment variables. Cached after first call.
    /// <list type="bullet">
    ///   <item><c>TEST_USE_LOCAL_SQL</c> — "true", "1", or "yes" (case-insensitive) enables local mode</item>
    ///   <item><c>SA_PASSWORD</c> — SQL Server SA password; fallback: "YourStrong!Passw0rd"</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Caching rationale: (1) Avoids repeated environment lookups for performance.
    /// (2) Guarantees all fixtures see identical config within a single xUnit run,
    /// preventing mid-session config drift if env vars were modified externally.
    /// </remarks>
    public static LocalSqlOptions FromEnvironment()
    {
        if (_cached is not null)
            return _cached;

        lock (_lock)
        {
            // Double-check after acquiring lock
            if (_cached is not null)
                return _cached;

            var localSqlEnv = Environment.GetEnvironmentVariable("TEST_USE_LOCAL_SQL") ?? string.Empty;
            var isEnabled = localSqlEnv.Equals("true", StringComparison.OrdinalIgnoreCase)
                         || localSqlEnv.Equals("1", StringComparison.OrdinalIgnoreCase)
                         || localSqlEnv.Equals("yes", StringComparison.OrdinalIgnoreCase);

            // Fallback matches src/host/AqtCoreFW.Api/appsettings.Development.json
            var saPassword = Environment.GetEnvironmentVariable("SA_PASSWORD")
                           ?? "YourStrong!Passw0rd";

            _cached = new LocalSqlOptions
            {
                IsEnabled = isEnabled,
                SaPassword = saPassword,
            };

            return _cached;
        }
    }

    /// <summary>Resets cached options. For testing purposes only.</summary>
    internal static void ResetCache() => _cached = null;
}
