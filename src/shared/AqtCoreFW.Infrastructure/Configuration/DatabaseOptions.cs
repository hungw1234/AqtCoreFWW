namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Database connection settings — injected from appsettings + Vault override in production.
/// Bound from configuration section "Database".
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
    public string Provider { get; set; } = "SqlServer";

    /// <summary>Threshold for slow query warning (ms). Default 100ms per perf spec.</summary>
    public int SlowQueryThresholdMs { get; set; } = 100;
}
