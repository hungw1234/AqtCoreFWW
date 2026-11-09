using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Token;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Singleton runtime key resolver: fetches per-version AES-256 keys from Vault KV v2.
/// Path format: <c>aqt/encryption/v{N}</c> (mount: secret/).
/// Cache TTL: 5 minutes sliding. THROWS KeyNotFoundException on Vault miss — no fallback.
/// </summary>
public sealed class VaultKeyResolver : IKeyResolver
{
    private readonly IVaultClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<VaultKeyResolver> _logger;
    private readonly int _currentVersion;

    // Size=1: required when host's IMemoryCache configures SizeLimit (Api host does, AuthServer does not).
    // No-op when SizeLimit unset, so safe everywhere.
    private static readonly MemoryCacheEntryOptions CacheOptions =
        new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(5))
            .SetSize(1);

    public VaultKeyResolver(
        IVaultClient client,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<VaultKeyResolver> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        _currentVersion = configuration.GetValue("Encryption:CurrentVersion", 1);
    }

    /// <inheritdoc/>
    public int CurrentVersion => _currentVersion;

    /// <inheritdoc/>
    public byte[] GetKey(int version)
    {
        var cacheKey = $"vault:encryption:v{version}";

        if (_cache.TryGetValue(cacheKey, out byte[]? cached) && cached is not null)
            return cached;

        var key = FetchFromVault(version);
        _cache.Set(cacheKey, key, CacheOptions);
        return key;
    }

    private byte[] FetchFromVault(int version)
    {
        try
        {
            // KV v2 mount "secret/"; path without mount prefix per VaultSharp convention
            var secret = _client.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(path: $"aqt/encryption/v{version}", mountPoint: "secret")
                .GetAwaiter().GetResult();

            var rawValue = secret?.Data?.Data;
            if (rawValue is null || !rawValue.TryGetValue("value", out var encoded) || encoded is null)
                throw new KeyNotFoundException($"Encryption key v{version} not found in Vault");

            var keyBytes = Convert.FromBase64String(encoded.ToString()!);

            if (keyBytes.Length != 32)
                throw new InvalidOperationException(
                    $"Vault key v{version} is {keyBytes.Length} bytes; expected 32 (AES-256).");

            _logger.LogInformation("Loaded encryption key v{Version} from Vault", version);
            return keyBytes;
        }
        catch (KeyNotFoundException)
        {
            throw; // propagate miss as-is
        }
        catch (Exception ex)
        {
            // Vault unreachable / auth failure — treat as miss (no fallback)
            _logger.LogError(ex, "Failed to fetch encryption key v{Version} from Vault", version);
            throw new KeyNotFoundException($"Encryption key v{version} not found in Vault", ex);
        }
    }
}

/// <summary>DI registration helper for <see cref="IKeyResolver"/> and <see cref="IVaultClient"/>.</summary>
public static class KeyResolverRegistration
{
    /// <summary>
    /// Registers <see cref="IVaultClient"/> and <see cref="IKeyResolver"/> as singletons.
    /// <see cref="IVaultClient"/> is registered separately so <see cref="VaultHealthCheck"/> can share it.
    /// Requires <see cref="IMemoryCache"/> to be registered before this call (AddMemoryCache).
    /// </summary>
    public static IServiceCollection AddVaultKeyResolver(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // IVaultClient singleton — shared by IKeyResolver + VaultHealthCheck
        services.AddSingleton<IVaultClient>(_ => BuildVaultClient(configuration));

        services.AddSingleton<IKeyResolver>(sp => new VaultKeyResolver(
            sp.GetRequiredService<IVaultClient>(),
            sp.GetRequiredService<IMemoryCache>(),
            configuration,
            sp.GetRequiredService<ILogger<VaultKeyResolver>>()));

        // Read-model decryptor — lets Dapper paths (which bypass the EF value converter) decrypt PII
        // columns through the same crypto. Singleton: stateless wrapper over the singleton key resolver.
        services.AddSingleton<IEncryptedFieldDecryptor, EncryptedFieldDecryptor>();

        return services;
    }

    internal static IVaultClient BuildVaultClient(IConfiguration configuration)
    {
        var opts = new VaultOptions();
        configuration.GetSection(VaultOptions.SectionName).Bind(opts);

        // Env vars override appsettings for Vault connection (same pattern as Api/Program.cs:79-86)
        var addr = Environment.GetEnvironmentVariable("VAULT_ADDR");
        if (!string.IsNullOrEmpty(addr)) opts.Address = addr;
        opts.Token = Environment.GetEnvironmentVariable("VAULT_TOKEN") ?? opts.Token;
        opts.RoleId = Environment.GetEnvironmentVariable("VAULT_ROLE_ID") ?? opts.RoleId;
        opts.SecretId = Environment.GetEnvironmentVariable("VAULT_SECRET_ID") ?? opts.SecretId;

        IAuthMethodInfo authMethod = !string.IsNullOrEmpty(opts.RoleId)
            ? new AppRoleAuthMethodInfo(opts.RoleId, opts.SecretId!)
            : new TokenAuthMethodInfo(opts.Token ?? "root");

        return new VaultClient(new VaultClientSettings(opts.Address, authMethod));
    }
}
