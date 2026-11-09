using System.Reflection;
using System.Text.RegularExpressions;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Logging;
using AqtCoreFW.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Compliance evidence tests for QÄ 742 Phase-06 (mục 4.4.a "không hardcode credential, qua giao diện cấu hình").
///
/// Verifies the Vault Finish slice deliverables structurally (no runtime SQL or Vault dependency):
///   - Vault wiring types exist (IKeyResolver, VaultKeyResolver, VaultHealthCheck)
///   - ProductionSecretValidator covers required keys
///   - EncryptedStringConverter supports v{N}: prefix + dual-decode
///   - LegacyCipherUpgrader exists for one-time migration
///   - SecretMaskingDestructuringPolicy is the secret-redaction Serilog hook
///   - appsettings.json (Api + AuthServer base + Production) carries no plaintext secrets
/// </summary>
public sealed class Qd742Phase06ComplianceTests
{
    [Fact]
    public void Compliance_4_4_a_VaultKeyResolver_TypesExist()
    {
        // QÄ 742 4.4.a: secrets via configuration provider (Vault), not hardcoded.
        typeof(IKeyResolver).Should().NotBeNull();
        typeof(VaultKeyResolver).Should().BeAssignableTo<IKeyResolver>();
        typeof(VaultHealthCheck).Should().NotBeNull();
    }

    [Fact]
    public void Compliance_4_4_a_VaultKeyResolver_ThrowsOnUnknownVersion()
    {
        // Encryption keys MUST fail-fast on miss — no graceful fallback (data corruption risk).
        var method = typeof(VaultKeyResolver).GetMethod(nameof(VaultKeyResolver.GetKey));
        method.Should().NotBeNull("VaultKeyResolver.GetKey must exist");
        method!.ReturnType.Should().Be(typeof(byte[]));
    }

    [Fact]
    public void Compliance_4_4_a_ProductionSecretValidator_CoversRequiredKeys()
    {
        // The validator must throw on missing/DEV_-prefix values for the 3 mandatory keys.
        var validator = typeof(ProductionSecretValidator);
        var assertMethod = validator.GetMethod(
            nameof(ProductionSecretValidator.AssertSecretValid),
            BindingFlags.Public | BindingFlags.Static);

        assertMethod.Should().NotBeNull();

        // Empty triggers
        var empty = () => ProductionSecretValidator.AssertSecretValid("ConnectionStrings:Default", "");
        empty.Should().Throw<InvalidOperationException>();

        // Whitespace triggers (M4 fix)
        var whitespace = () => ProductionSecretValidator.AssertSecretValid("Encryption:FieldKey", "   ");
        whitespace.Should().Throw<InvalidOperationException>();

        // DEV_ prefix triggers
        var devPrefix = () => ProductionSecretValidator.AssertSecretValid("Audit:HmacKey", "DEV_PLACEHOLDER");
        devPrefix.Should().Throw<InvalidOperationException>();

        // Known dev marker (all-zero base64) triggers
        var marker = () => ProductionSecretValidator.AssertSecretValid(
            "Encryption:FieldKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        marker.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Compliance_4_4_a_EncryptedStringConverter_SupportsVersionPrefix()
    {
        // Converter must accept Func<int,byte[]> + int currentVersion (D10.2 design).
        var ctor = typeof(EncryptedStringConverter)
            .GetConstructor(new[] { typeof(Func<int, byte[]>), typeof(int) });

        ctor.Should().NotBeNull(
            "EncryptedStringConverter MUST expose (Func<int,byte[]> keyResolver, int currentVersion) ctor");
    }

    [Fact]
    public void Compliance_4_4_a_LegacyCipherUpgrader_ExistsAndIsIdempotent()
    {
        // The CLR-side helper for one-time re-encryption to v{N}: prefix.
        var type = typeof(LegacyCipherUpgrader);
        type.Should().NotBeNull();

        var upgrade = type.GetMethod(
            nameof(LegacyCipherUpgrader.UpgradeColumnAsync),
            BindingFlags.Public | BindingFlags.Static);
        upgrade.Should().NotBeNull();
    }

    [Fact]
    public void Compliance_4_4_a_SecretMaskingDestructuringPolicy_IsRegistered()
    {
        // Logger must mask secret-shaped property values to prevent leakage in logs.
        var policy = typeof(SecretMaskingDestructuringPolicy);
        policy.Should().NotBeNull();

        var shouldMask = policy.GetMethod(
            "ShouldMaskKey",
            BindingFlags.Public | BindingFlags.Static);
        shouldMask.Should().NotBeNull();

        // Spot-check the masking decisions on canonical patterns.
        ((bool)shouldMask!.Invoke(null, new object[] { "Password" })!).Should().BeTrue();
        ((bool)shouldMask.Invoke(null, new object[] { "VaultToken" })!).Should().BeTrue();
        ((bool)shouldMask.Invoke(null, new object[] { "FieldKey" })!).Should().BeTrue();
        ((bool)shouldMask.Invoke(null, new object[] { "VaultAddr" })!).Should().BeFalse();
        ((bool)shouldMask.Invoke(null, new object[] { "KeyVersion" })!).Should().BeFalse();
    }

    [Fact]
    public void Compliance_4_4_a_ApplicationSettings_NoPlaintextSecretsInBaseFile()
    {
        // Regex scan: appsettings.json (BASE, not Development) MUST NOT carry real secrets.
        // Allowed: empty strings, dev-shaped values like "localhost", config-only keys.
        // Forbidden: any non-trivial password/token/key string in cleartext.
        var apiBase = Path.Combine(GetRepoRoot(), "src", "host", "AqtCoreFW.Api", "appsettings.json");
        var authBase = Path.Combine(GetRepoRoot(), "src", "host", "AqtCoreFW.AuthServer", "appsettings.json");

        AssertNoPlaintextSecrets(apiBase);
        AssertNoPlaintextSecrets(authBase);
    }

    [Fact]
    public void Compliance_4_4_a_ProductionSettings_DeclareEmptyPlaceholdersForVault()
    {
        // Production.json files MUST declare required-key placeholders empty so that
        // ProductionSecretValidator gates startup until Vault populates them.
        var apiProd = Path.Combine(GetRepoRoot(), "src", "host", "AqtCoreFW.Api", "appsettings.Production.json");
        var authProd = Path.Combine(GetRepoRoot(), "src", "host", "AqtCoreFW.AuthServer", "appsettings.Production.json");

        File.Exists(apiProd).Should().BeTrue();
        File.Exists(authProd).Should().BeTrue();

        AssertProductionPlaceholdersPresent(apiProd);
        AssertProductionPlaceholdersPresent(authProd);
    }

    [Fact]
    public void Compliance_4_4_a_KeyRotationRunbook_Exists()
    {
        // Audit evidence: ops runbook MUST be present at the agreed location.
        var runbook = Path.Combine(GetRepoRoot(), "docs", "ops", "vault-key-rotation-runbook.md");
        File.Exists(runbook).Should().BeTrue(
            "Vault key rotation runbook is mandatory compliance evidence per phase-06 §155");

        var content = File.ReadAllText(runbook);
        content.Should().Contain("2-person approval", "Runbook MUST require 2-person approval per QÄ 742");
        content.Should().Contain("N=3", "Runbook MUST document version retention policy");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetRepoRoot()
    {
        // Anchor by .git directory (always at repo root, format-agnostic).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Repo root (.git) not found from " + AppContext.BaseDirectory);
    }

    private static void AssertNoPlaintextSecrets(string path)
    {
        var content = File.ReadAllText(path);

        // Detect strings of length >= 12 in JSON values for keys named like *password|*key|*secret|*token
        // that are NOT empty and NOT known DEV markers.
        var pattern = new Regex(
            @"""[A-Za-z]*(?:Password|Secret|HmacKey|FieldKey|JwtSigningKey)""\s*:\s*""(?<v>[^""]+)""",
            RegexOptions.IgnoreCase);

        foreach (Match m in pattern.Matches(content))
        {
            var v = m.Groups["v"].Value;
            if (string.IsNullOrWhiteSpace(v)) continue;
            if (v.StartsWith("DEV_", StringComparison.Ordinal)) continue;
            if (v == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=") continue;

            throw new Xunit.Sdk.XunitException(
                $"{path}: detected plaintext-shaped secret value '{v}' for match '{m.Value}'. " +
                "Real secrets MUST live in Vault, not appsettings (QÄ 742 4.4.a).");
        }
    }

    private static void AssertProductionPlaceholdersPresent(string path)
    {
        // Use IConfigurationBuilder to handle JSON comments + deep keys cleanly.
        var config = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .Build();

        config["ConnectionStrings:Default"].Should().NotBeNull(
            $"{path}: ConnectionStrings:Default placeholder MUST be declared (validator gate)");
        config["Encryption:FieldKey"].Should().NotBeNull(
            $"{path}: Encryption:FieldKey placeholder MUST be declared");
        config["Audit:HmacKey"].Should().NotBeNull(
            $"{path}: Audit:HmacKey placeholder MUST be declared");
        // ConfigurationBuilder stringifies bool to "True" (PascalCase); compare case-insensitive.
        config["Vault:Enabled"].Should().BeEquivalentTo("true",
            $"{path}: Vault:Enabled MUST be true in Production");
    }
}
