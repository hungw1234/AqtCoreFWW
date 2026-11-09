using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VaultSharp;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Singleton runtime pepper resolver: fetches per-tenant pepper bytes from Vault KV v2.
/// Path format: <c>aqt/encryption/email-pepper-{tenantId:N}</c> (mount: secret/).
/// Cache TTL: 5 minutes sliding. THROWS KeyNotFoundException on Vault miss — no fallback.
/// </summary>
public sealed class VaultPepperResolver : IPepperResolver
{
    private readonly IVaultClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<VaultPepperResolver> _logger;

    // Size=1: required when host's IMemoryCache configures SizeLimit (Api host does, AuthServer does not).
    // No-op when SizeLimit unset, so safe everywhere.
    private static readonly MemoryCacheEntryOptions CacheOptions =
        new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(5))
            .SetSize(1);

    public VaultPepperResolver(
        IVaultClient client,
        IMemoryCache cache,
        ILogger<VaultPepperResolver> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public byte[] GetPepper(Guid tenantId)
    {
        var cacheKey = $"vault:pepper:{tenantId:N}";

        if (_cache.TryGetValue(cacheKey, out byte[]? cached) && cached is not null)
            return cached;

        var pepper = FetchFromVault(tenantId);
        _cache.Set(cacheKey, pepper, CacheOptions);
        return pepper;
    }

    private byte[] FetchFromVault(Guid tenantId)
    {
        try
        {
            // KV v2 mount "secret/"; path without mount prefix per VaultSharp convention
            var secret = _client.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(path: $"aqt/encryption/email-pepper-{tenantId:N}", mountPoint: "secret")
                .GetAwaiter().GetResult();

            var rawValue = secret?.Data?.Data;
            if (rawValue is null || !rawValue.TryGetValue("value", out var encoded) || encoded is null)
                throw new KeyNotFoundException($"Email pepper for tenant {tenantId:N} not found in Vault");

            var pepperBytes = Convert.FromBase64String(encoded.ToString()!);

            _logger.LogInformation("Loaded email pepper for tenant {TenantId} from Vault", tenantId.ToString("N"));
            return pepperBytes;
        }
        catch (KeyNotFoundException)
        {
            throw; // propagate miss as-is
        }
        catch (Exception ex)
        {
            // Vault unreachable / auth failure — treat as miss (no fallback)
            _logger.LogError(ex, "Failed to fetch email pepper for tenant {TenantId} from Vault", tenantId.ToString("N"));
            throw new KeyNotFoundException($"Email pepper for tenant {tenantId:N} not found in Vault", ex);
        }
    }
}

/// <summary>DI registration helper for <see cref="IPepperResolver"/>.</summary>
public static class PepperResolverRegistration
{
    /// <summary>
    /// Registers <see cref="IPepperResolver"/> as a singleton.
    /// Requires <see cref="IVaultClient"/> already registered (call <c>AddVaultKeyResolver</c> first).
    /// Requires <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> registered (AddMemoryCache).
    /// </summary>
    public static IServiceCollection AddVaultPepperResolver(this IServiceCollection services)
    {
        // IVaultClient is NOT registered here — already registered by AddVaultKeyResolver.
        services.AddSingleton<IPepperResolver>(sp => new VaultPepperResolver(
            sp.GetRequiredService<IVaultClient>(),
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<ILogger<VaultPepperResolver>>()));

        return services;
    }
}
