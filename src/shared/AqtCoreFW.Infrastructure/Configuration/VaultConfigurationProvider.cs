using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Token;

namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// IConfigurationSource for HashiCorp Vault KV v2 secrets.
/// Loads all keys from a single secret path and flattens into IConfiguration keys.
/// Graceful fallback: logs warning if Vault unreachable — env vars still work.
/// </summary>
public sealed class VaultConfigurationSource : IConfigurationSource
{
    private readonly VaultOptions _options;

    public VaultConfigurationSource(VaultOptions options) => _options = options;

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new VaultConfigurationProvider(_options);
}

public sealed class VaultConfigurationProvider : ConfigurationProvider
{
    private readonly VaultOptions _options;

    public VaultConfigurationProvider(VaultOptions options) => _options = options;

    public override void Load()
    {
        try
        {
            LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Graceful fallback — env vars / appsettings still provide config
            Console.WriteLine($"[Vault] Warning: Could not load secrets from Vault: {ex.Message}");
        }
    }

    private async Task LoadAsync()
    {
        IAuthMethodInfo authMethod = !string.IsNullOrEmpty(_options.RoleId)
            ? new AppRoleAuthMethodInfo(_options.RoleId, _options.SecretId!)
            : new TokenAuthMethodInfo(_options.Token ?? "root");

        var settings = new VaultClientSettings(_options.Address, authMethod);
        var client = new VaultClient(settings);

        // KV v2: SecretPath "secret/data/aqtcorefw" splits into mountPoint "secret" + path
        // "aqtcorefw". mountPoint MUST be passed explicitly — VaultSharp's default is "kv-v2",
        // which 404s ("no handler for route kv-v2/data/...") against the standard "secret"
        // mount. Caught on the first-ever Production boot 2026-07-23: this provider had never
        // executed before (Vault:Enabled is false outside Production).
        var segments = _options.SecretPath.Split('/', 3);
        var (mountPoint, path) = segments.Length == 3 && segments[1] == "data"
            ? (segments[0], segments[2])
            : ("secret", _options.SecretPath);
        var secret = await client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: path, mountPoint: mountPoint);

        if (secret?.Data?.Data == null) return;

        foreach (var kvp in secret.Data.Data)
        {
            // Vault keys use __ as separator (same as env vars): ConnectionStrings__Default
            // Convert to : for .NET config: ConnectionStrings:Default
            var key = kvp.Key.Replace("__", ":");
            Data[key] = kvp.Value?.ToString() ?? string.Empty;
        }
    }
}

/// <summary>Extension method to register Vault config provider.</summary>
public static class VaultConfigurationExtensions
{
    public static IConfigurationBuilder AddVaultConfiguration(
        this IConfigurationBuilder builder,
        Action<VaultOptions> configure)
    {
        var options = new VaultOptions();
        configure(options);

        if (!options.Enabled) return builder;

        return builder.Add(new VaultConfigurationSource(options));
    }

    /// <summary>
    /// Registers the Vault config provider using the standard host wiring: bind the
    /// <see cref="VaultOptions"/> section, then let environment variables override the Vault
    /// connection settings (VAULT_ADDR / VAULT_TOKEN / VAULT_ROLE_ID / VAULT_SECRET_ID /
    /// VAULT_SECRET_PATH). Shared by Api and AuthServer so the env-override precedence stays
    /// defined in exactly one place. Must run before GetConnectionString so a Vault-sourced
    /// connection string is available.
    /// </summary>
    public static IConfigurationBuilder AddVaultConfiguration(
        this IConfigurationBuilder builder,
        IConfiguration sourceConfiguration) =>
        builder.AddVaultConfiguration(opts =>
        {
            sourceConfiguration.GetSection(VaultOptions.SectionName).Bind(opts);
            // Env vars override appsettings for the Vault connection itself.
            var addr = Environment.GetEnvironmentVariable("VAULT_ADDR");
            if (!string.IsNullOrEmpty(addr)) opts.Address = addr;
            opts.Token = Environment.GetEnvironmentVariable("VAULT_TOKEN") ?? opts.Token;
            opts.RoleId = Environment.GetEnvironmentVariable("VAULT_ROLE_ID") ?? opts.RoleId;
            opts.SecretId = Environment.GetEnvironmentVariable("VAULT_SECRET_ID") ?? opts.SecretId;
            var path = Environment.GetEnvironmentVariable("VAULT_SECRET_PATH");
            if (!string.IsNullOrEmpty(path)) opts.SecretPath = path;
        });
}
