namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Represents a short-lived inbound authentication token issued to Viettel Mysign.
///
/// <para>Flow: BE issues a raw 32-byte random token to Mysign via the /auth callback endpoint.
/// Only the SHA-256 hash is persisted — the raw token is returned once to Mysign and never stored.
/// Mysign presents the raw token as a Bearer credential in subsequent /result callbacks;
/// BE hashes it and validates against this record.</para>
///
/// <para>Schema: [identity] (via IdentityDbContext default schema).</para>
/// <para>PK: <see cref="TokenHash"/> (SHA-256 hex, uppercase) — raw token never persisted.</para>
/// </summary>
public sealed class MysignInboundToken
{
    /// <summary>
    /// SHA-256 hex (uppercase) of the raw base64 token.
    /// PK — raw token never stored in DB.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>UTC timestamp when this token was issued.</summary>
    public DateTime IssuedAt { get; private set; }

    /// <summary>UTC timestamp after which the token is no longer valid.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>UTC timestamp of the most recent successful validation (null if not yet used).</summary>
    public DateTime? LastUsedAt { get; private set; }

    /// <summary>True if explicitly revoked before natural expiry.</summary>
    public bool IsRevoked { get; private set; }

    /// <summary>EF Core: private parameterless constructor for materialisation.</summary>
    private MysignInboundToken() { }

    /// <summary>
    /// Factory — creates a new token record.
    /// </summary>
    /// <param name="tokenHash">SHA-256 hex (uppercase) of the raw token. Pre-computed by caller.</param>
    /// <param name="ttl">Token lifetime (typically 1 hour per spec).</param>
    public static MysignInboundToken Issue(string tokenHash, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("TokenHash must not be empty.", nameof(tokenHash));
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("TTL must be positive.", nameof(ttl));

        var now = DateTime.UtcNow;
        return new MysignInboundToken
        {
            TokenHash = tokenHash,
            IssuedAt  = now,
            ExpiresAt = now.Add(ttl),
            IsRevoked = false,
        };
    }

    /// <summary>Updates LastUsedAt to now — called on successful validation.</summary>
    public void MarkUsed()
    {
        LastUsedAt = DateTime.UtcNow;
    }

    /// <summary>Explicitly revokes the token before natural expiry.</summary>
    public void Revoke()
    {
        IsRevoked = true;
    }

    /// <summary>
    /// Returns true if the token is currently valid — not revoked and not expired.
    /// </summary>
    /// <param name="now">Current UTC time (injected so tests can control time).</param>
    public bool IsValid(DateTime now) => !IsRevoked && now < ExpiresAt;
}
