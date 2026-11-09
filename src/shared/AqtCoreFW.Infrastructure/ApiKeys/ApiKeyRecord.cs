namespace AqtCoreFW.Infrastructure.ApiKeys;

/// <summary>
/// Persisted API key record — key format: aqt_{prefix8}_{random24}.
/// KeyHash = SHA-256(plaintext). Plaintext returned once at creation only (GitHub PAT pattern).
/// Stored in schema "gateway" via ApiKeyDbContext.
/// </summary>
public sealed class ApiKeyRecord
{
    public Guid Id { get; set; }

    /// <summary>Human-readable label for the key (e.g. "service-a-prod").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>First 8 chars of the random segment — used as fast DB lookup prefix.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of the full plaintext key.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>OAuth2 client_id claim for claims-based identity.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Granted scopes stored as JSON array (e.g. ["cases:read","cases:write"]).</summary>
    public string ScopesJson { get; set; } = "[]";

    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public Guid TenantId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<ApiKeyScope> Scopes { get; set; } = [];
}
