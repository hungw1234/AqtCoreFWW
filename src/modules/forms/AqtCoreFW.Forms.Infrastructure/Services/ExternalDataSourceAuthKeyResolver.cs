using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VaultSharp;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Reads the per-data-source API key for an authenticated external data source from Vault
/// (KV v2, mount <c>secret/</c>, path <c>aqt/forms/external-ds/{vaultKeyId}/api-key</c>, field <c>value</c>),
/// cached in-process for 5 minutes. Mirrors <c>VaultAuditHmacKeyResolver</c> but WITHOUT any legacy-config
/// fallback — an external data-source key has no legacy source, so a Vault miss simply yields
/// <see langword="null"/> and the caller fails closed (returns no options) rather than fetching unauthenticated.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IVaultClient"/> is injected as OPTIONAL (<see langword="null"/>): it is registered only on the
/// Api host (via <c>AddVaultKeyResolver</c>). On a host or test context without Vault wired, this resolver is
/// constructed with a null client and every lookup returns null — the authed-external path degrades to
/// fail-closed empty, and the static external path is unaffected because it never calls this resolver.
/// </para>
/// <para>
/// <c>vaultKeyId</c> MUST be a server-side data-source identifier taken from the form template config,
/// never a client-supplied string, so a caller cannot traverse the Vault path. The caller is responsible
/// for that invariant; this resolver treats the value as opaque.
/// </para>
/// </remarks>
public sealed class ExternalDataSourceAuthKeyResolver(
    IMemoryCache cache,
    ILogger<ExternalDataSourceAuthKeyResolver> logger,
    IVaultClient? vaultClient = null)
{
    // Size=1: required when the host's IMemoryCache configures a SizeLimit (the Api host does).
    private static readonly MemoryCacheEntryOptions CacheOptions =
        new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(5))
            .SetSize(1);

    private int _missWarningEmitted; // 0 = not yet, 1 = already; Interlocked-guarded (warn-once per process)

    /// <summary>
    /// Returns the Vault-stored API key for <paramref name="vaultKeyId"/>, or <see langword="null"/> when
    /// Vault is not wired, the secret is absent, or Vault is unreachable. Never throws — the caller fails closed.
    /// </summary>
    public async Task<string?> GetApiKeyAsync(string vaultKeyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vaultKeyId) || vaultClient is null)
            return null;

        var cacheKey = $"vault:forms:ext-ds:{vaultKeyId}:api-key";
        if (cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return cached;

        try
        {
            var secret = await vaultClient.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(path: $"aqt/forms/external-ds/{vaultKeyId}/api-key", mountPoint: "secret")
                .WaitAsync(cancellationToken);

            var data = secret?.Data?.Data;
            if (data is null || !data.TryGetValue("value", out var raw) || raw is null)
            {
                WarnMissOnce(vaultKeyId, exception: null);
                return null;
            }

            var key = raw.ToString();
            if (string.IsNullOrEmpty(key))
            {
                WarnMissOnce(vaultKeyId, exception: null);
                return null;
            }

            cache.Set(cacheKey, key, CacheOptions);
            return key;
        }
        catch (Exception ex)
        {
            // Vault unreachable / auth failure / secret missing → fail closed. Never leak the key or the
            // exception to the caller; the authed-external resolve degrades to an empty option list.
            WarnMissOnce(vaultKeyId, ex);
            return null;
        }
    }

    private void WarnMissOnce(string vaultKeyId, Exception? exception)
    {
        // Interlocked.CompareExchange returns the ORIGINAL value before the swap; 0 means we are first.
        if (Interlocked.CompareExchange(ref _missWarningEmitted, 1, 0) != 0)
            return;

        logger.LogWarning(exception,
            "External data-source API key not available from Vault (path aqt/forms/external-ds/{VaultKeyId}/api-key); "
            + "authed-external option resolution is failing closed (empty options).",
            vaultKeyId);
    }
}
