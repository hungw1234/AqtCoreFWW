using System.Text;
using AqtCoreFW.Audit.Application.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VaultSharp;

namespace AqtCoreFW.Audit.Infrastructure.Services;

/// <summary>
/// Singleton runtime resolver for the Audit HMAC-SHA256 chain key.
/// Path: <c>aqt/audit/hmac-v{Audit:Hmac:CurrentVersion ?? 1}</c> (mount: secret/), KV v2.
/// Returns raw 32 bytes (Base64-decoded from Vault's <c>value</c> field).
/// Cache TTL: 5min sliding.
/// </summary>
/// <remarks>
/// Legacy fallback: on Vault miss, if <c>IConfiguration["Audit:HmacKey"]</c> is present, decode
/// as Base64; if that fails, fall back to UTF-8 bytes (legacy behavior). A single LogWarning is
/// emitted per process. Removed in a future release (tracked in backlog).
/// </remarks>
public sealed class VaultAuditHmacKeyResolver : IAuditHmacKeyResolver
{
    private readonly IVaultClient _client;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VaultAuditHmacKeyResolver> _logger;
    private readonly int _currentVersion;
    private int _legacyWarningEmitted; // 0 = not yet, 1 = already; Interlocked-guarded

    // Size=1: required when host's IMemoryCache configures SizeLimit (Api host does).
    private static readonly MemoryCacheEntryOptions CacheOptions =
        new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(5))
            .SetSize(1);

    public VaultAuditHmacKeyResolver(
        IVaultClient client,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<VaultAuditHmacKeyResolver> logger)
    {
        _client = client;
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
        _currentVersion = configuration.GetValue("Audit:Hmac:CurrentVersion", 1);
    }

    /// <inheritdoc/>
    public byte[] GetKey()
    {
        var cacheKey = $"vault:audit:hmac:v{_currentVersion}";

        if (_cache.TryGetValue(cacheKey, out byte[]? cached) && cached is not null)
            return cached;

        var key = FetchFromVaultOrLegacy(_currentVersion);
        _cache.Set(cacheKey, key, CacheOptions);
        return key;
    }

    private byte[] FetchFromVaultOrLegacy(int version)
    {
        try
        {
            var secret = _client.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(path: $"aqt/audit/hmac-v{version}", mountPoint: "secret")
                .GetAwaiter().GetResult();

            var rawValue = secret?.Data?.Data;
            if (rawValue is null || !rawValue.TryGetValue("value", out var encoded) || encoded is null)
                throw new KeyNotFoundException($"Audit HMAC key v{version} not found in Vault");

            var keyBytes = Convert.FromBase64String(encoded.ToString()!);

            if (keyBytes.Length != 32)
                throw new InvalidOperationException(
                    $"Vault audit HMAC key v{version} is {keyBytes.Length} bytes; expected 32.");

            _logger.LogInformation("Loaded audit HMAC key v{Version} from Vault", version);
            return keyBytes;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Vault miss / unreachable / auth failure / KeyNotFoundException → try legacy config fallback.
            // InvalidOperationException (length mismatch from Vault success path) escapes — that's a real misconfig.
            return TryLegacyConfigFallback(version, ex)
                ?? throw new KeyNotFoundException(
                    $"Audit HMAC key v{version} not found in Vault and no legacy Audit:HmacKey config", ex);
        }
    }

    private byte[]? TryLegacyConfigFallback(int version, Exception vaultException)
    {
        var legacy = _configuration["Audit:HmacKey"];
        if (string.IsNullOrEmpty(legacy))
            return null;

        // Warn-once: Interlocked.CompareExchange returns the ORIGINAL value before swap; if it was 0, we're first.
        if (Interlocked.CompareExchange(ref _legacyWarningEmitted, 1, 0) == 0)
        {
            _logger.LogWarning(vaultException,
                "Audit HMAC key sourced from legacy Audit:HmacKey config — Vault path aqt/audit/hmac-v{Version} preferred. Will be removed in next release.",
                version);
        }

        // Prefer Base64 (matches Vault format); fall back to UTF-8 bytes (legacy literal-string keys).
        try
        {
            return Convert.FromBase64String(legacy);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(legacy);
        }
    }
}
