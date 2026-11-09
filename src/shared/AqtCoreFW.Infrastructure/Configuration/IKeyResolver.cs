namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Runtime per-version encryption key resolver backed by HashiCorp Vault.
/// THROWS on miss — a missing encryption key indicates data corruption risk, not a config gap.
/// </summary>
public interface IKeyResolver
{
    /// <summary>The current active key version read from <c>Encryption:CurrentVersion</c> config (default 1).</summary>
    int CurrentVersion { get; }

    /// <summary>
    /// Fetches the raw 32-byte AES-256 key for the given version.
    /// Cached for 5 minutes (sliding).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Vault has no key for <paramref name="version"/>.</exception>
    byte[] GetKey(int version);
}
