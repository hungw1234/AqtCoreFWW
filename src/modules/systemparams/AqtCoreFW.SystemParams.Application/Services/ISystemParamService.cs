namespace AqtCoreFW.SystemParams.Application.Services;

/// <summary>
/// Runtime typed config access. Register as singleton (IServiceScopeFactory for scoped DB).
/// Cache-aside (TTL 5min). Tenant override falls back to system-wide default.
/// SetAsync persists + invalidates cache + (for feature:*) publishes Redis invalidation.
/// </summary>
public interface ISystemParamService
{
    // --- Non-generic string access (used by SessionConfigController etc.) ---

    /// <summary>Raw string read. Returns null if key absent.</summary>
    Task<string?> GetAsync(string key, string? tenantId = null, CancellationToken ct = default);

    /// <summary>Create-or-update system-wide param. Value serialised (JSON for complex types).</summary>
    Task SetAsync(string key, object value, CancellationToken ct = default);

    /// <summary>Create-or-update tenant-scoped param.</summary>
    Task SetAsync(string key, object value, string tenantId, CancellationToken ct = default);

    // --- Typed access (throws KeyNotFoundException on miss — parity with legacy API) ---

    /// <summary>Get system-wide param, deserialised to T. Throws KeyNotFoundException if missing.</summary>
    Task<T> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>Get tenant-specific param, falls back to system-wide default. Throws if neither exists.</summary>
    Task<T> GetAsync<T>(string key, string tenantId, CancellationToken ct = default);

    /// <summary>Get with explicit default — no exception if key not found.</summary>
    Task<T> GetOrDefaultAsync<T>(string key, T defaultValue, CancellationToken ct = default);
}
