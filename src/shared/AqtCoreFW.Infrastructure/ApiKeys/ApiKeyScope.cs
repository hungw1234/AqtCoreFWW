namespace AqtCoreFW.Infrastructure.ApiKeys;

/// <summary>
/// Tenant-scoped permission grant for an API key.
/// One key can have multiple scopes across tenants (cross-tenant M2M).
/// ScopePermission examples: "cases:read", "cases:write", "notifications:send".
/// </summary>
public sealed class ApiKeyScope
{
    public Guid Id { get; set; }
    public Guid ApiKeyId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Permission identifier in format "{module}:{action}" (e.g. "cases:read").</summary>
    public string ScopePermission { get; set; } = string.Empty;

    // Navigation
    public ApiKeyRecord ApiKey { get; set; } = null!;
}
