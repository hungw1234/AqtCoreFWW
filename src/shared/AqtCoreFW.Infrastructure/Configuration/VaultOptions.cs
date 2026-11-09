namespace AqtCoreFW.Infrastructure.Configuration;

/// <summary>
/// Vault connection options — loaded from appsettings Vault section + env vars.
/// Supports Token auth (dev) and AppRole auth (production).
/// </summary>
public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    public bool Enabled { get; set; }
    public string Address { get; set; } = "http://localhost:8200";
    public string SecretPath { get; set; } = "secret/data/aqtcorefw";
    public string? Token { get; set; }
    public string? RoleId { get; set; }
    public string? SecretId { get; set; }
}
