using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guards for the audit-hmac-vault-seeder plan (P5):
///
///   1. <see cref="IConfiguration_SecretKey_Reads_AreAllowlisted"/> — scan source
///      under <c>src/</c> for direct <c>configuration["..."]</c> reads with key names
///      that look like secret material (Hmac / Pepper / EncryptionKey / SecretKey).
///      Allowlist: <see cref="AllowlistedSourceFiles"/> — the two resolver implementations
///      that legitimately read these keys (one for production guard, one as the legacy
///      fallback on top of Vault). New occurrences anywhere else are a regression.
///
///   2. <see cref="AuthServerHost_DoesNotResolve_IAuditHmacKeyResolver"/> — AuthServer
///      is a Hangfire CLIENT (it enqueues HMAC jobs but never executes ComputeAsync).
///      Registering <c>IAuditHmacKeyResolver</c> there would silently re-introduce the
///      cross-host config drift fixed in Phase-01. Guard: AuthServer's Program.cs must
///      NOT call <c>AddAuditInfrastructure(...)</c>; Api's MUST.
/// </summary>
public sealed class SecretConfigBypassGuardTests
{
    /// <summary>
    /// Repo-relative source paths permitted to read secret-flavored config keys.
    /// Keep MINIMAL — adding entries requires explicit code-review approval.
    /// </summary>
    private static readonly string[] AllowlistedSourceFiles =
    {
        // Production startup guard — scans config by name, must reference the key strings.
        Path.Combine("src", "shared", "AqtCoreFW.Infrastructure", "Configuration", "ProductionSecretValidator.cs"),
        // Audit HMAC resolver — owns the legacy `Audit:HmacKey` fallback (1-release deprecation).
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Infrastructure", "Services", "VaultAuditHmacKeyResolver.cs"),
    };

    // Substring match on substantive secret-name tokens. NOT word-boundary —
    // compound names like `HmacKey`, `EmailHashPepper` must be caught, and curated
    // alt-list keeps false positives low. (`HmacRetentionDays` would technically
    // match `Hmac`; expect that retention-flavored knobs use Options binding, not
    // direct `_config["..."]` reads. Add explicit allowlist if/when needed.)
    private static readonly Regex SecretKeyPattern = new(
        """(configuration|cfg|_configuration|_config|builder\.Configuration)\s*\[\s*"[^"]*(Hmac|Pepper|EncryptionKey|FieldKey|SecretKey)[^"]*"\s*\]""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void IConfiguration_SecretKey_Reads_AreAllowlisted()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        Directory.Exists(srcRoot).Should().BeTrue($"src/ must exist under {repoRoot}");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsAllowlisted(file, repoRoot)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var text = File.ReadAllText(file);
            foreach (Match match in SecretKeyPattern.Matches(text))
            {
                var lineNo = text[..match.Index].Count(c => c == '\n') + 1;
                violations.Add($"{Path.GetRelativePath(repoRoot, file)}:{lineNo}: {match.Value.Trim()}");
            }
        }

        violations.Should().BeEmpty(
            "secret-flavored IConfiguration[\"...\"] reads must live in an allowlisted resolver " +
            "(ProductionSecretValidator OR VaultAuditHmacKeyResolver). " +
            "Add a typed resolver instead of reading the key directly. " +
            "Allowlist: {0}",
            string.Join(", ", AllowlistedSourceFiles));
    }

    [Fact]
    public void SecretKeyPattern_MatchesCompoundCompoundKeyNames()
    {
        // Snapshot test (per phase-05 plan risk-mitigation): assert the regex catches
        // the canonical compound secret names. Failing here means the guard itself
        // is too narrow and would let real regressions slip past.
        var positives = new[]
        {
            """_config["Audit:HmacKey"]""",
            """configuration["Encryption:FieldKey"]""",
            """cfg["EmailHashPepper"]""",
            """builder.Configuration["Module:SecretKey"]""",
            """_configuration["Some:Module:EncryptionKey"]""",
        };
        foreach (var sample in positives)
            SecretKeyPattern.IsMatch(sample).Should().BeTrue($"sample must match: {sample}");

        // Negative: nothing secret-flavored in the key name. Note: any bracket-indexer
        // read of a key containing the substring `Hmac`/`Pepper`/etc. is presumed-secret;
        // non-secret knobs (versions, retention) must use `GetValue<T>` or Options binding
        // to stay outside this guard's scope.
        var negatives = new[]
        {
            """configuration["ConnectionStrings:Default"]""",
            """cfg["Hangfire:DashboardPath"]""",
            """_config["Logging:LogLevel:Default"]""",
            """configuration.GetValue("Audit:Hmac:CurrentVersion", 1)""", // GetValue, not indexer → out of scope
        };
        foreach (var sample in negatives)
            SecretKeyPattern.IsMatch(sample).Should().BeFalse($"sample must NOT match: {sample}");
    }

    [Fact]
    public void AuthServerHost_DoesNotResolve_IAuditHmacKeyResolver()
    {
        var repoRoot = FindRepoRoot();
        var authProgram = Path.Combine(repoRoot, "src", "host", "AqtCoreFW.AuthServer", "Program.cs");
        var apiProgram = Path.Combine(repoRoot, "src", "host", "AqtCoreFW.Api", "Program.cs");

        File.Exists(authProgram).Should().BeTrue(authProgram);
        File.Exists(apiProgram).Should().BeTrue(apiProgram);

        var authText = File.ReadAllText(authProgram);
        var apiText = File.ReadAllText(apiProgram);

        // Match the actual DI extension call. Note: AuthServer may MENTION the symbol in a
        // comment explaining the asymmetry; allow comments by requiring the dot-prefix +
        // call-syntax on a non-comment line.
        var callPattern = new Regex(
            @"^\s*[^/].*\.AddAuditInfrastructure\s*\(",
            RegexOptions.Multiline);

        callPattern.IsMatch(apiText).Should().BeTrue(
            "Api/Program.cs MUST call AddAuditInfrastructure (Api host owns HMAC compute jobs)");

        callPattern.IsMatch(authText).Should().BeFalse(
            "AuthServer/Program.cs MUST NOT call AddAuditInfrastructure — AuthServer is a Hangfire " +
            "CLIENT for HMAC jobs only. Registering IAuditHmacKeyResolver there silently " +
            "re-introduces the cross-host config drift fixed in Phase-01.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsAllowlisted(string absoluteFile, string repoRoot)
    {
        var relative = Path.GetRelativePath(repoRoot, absoluteFile);
        return AllowlistedSourceFiles.Any(allowed =>
            string.Equals(relative, allowed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Walks up from the test assembly location until it finds a directory containing
    /// both <c>src/</c> and <c>tests/</c> — that is the repo root. Tests run from
    /// <c>tests/.../bin/Debug/net10.0</c>; the solution file lives under <c>src/</c>.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(
            Directory.Exists(Path.Combine(dir.FullName, "src")) &&
            Directory.Exists(Path.Combine(dir.FullName, "tests"))))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("could not locate repo root (looked for src/ + tests/ walking up from " + AppContext.BaseDirectory + ")");
        return dir!.FullName;
    }
}
