namespace AqtCoreFW.Audit.Application.Services;

/// <summary>
/// Runtime resolver for the symmetric HMAC-SHA256 key used by <c>HmacChainService</c>.
/// Sourced from Vault path <c>aqt/audit/hmac-v{N}</c> via Infrastructure (<c>VaultAuditHmacKeyResolver</c>).
/// Option C: single active version per release; rotation deferred (no version param).
/// </summary>
public interface IAuditHmacKeyResolver
{
    /// <summary>
    /// Returns the raw 32-byte HMAC key for the currently active version.
    /// Cached internally (5min sliding).
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// Vault has no key at the active path and no legacy <c>Audit:HmacKey</c> config fallback is present.
    /// </exception>
    byte[] GetKey();
}
