using System.Text.RegularExpressions;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard: every environment connection string for "ConnectionStrings:Default"
/// must contain <c>MultipleActiveResultSets=true</c> (case-insensitive).
///
/// Why this matters: the ambient cross-module unit of work shares ONE open SqlConnection
/// across multiple DbContexts and Dapper reads. MARS allows interleaved result sets on a
/// single connection — e.g. a Dapper identity read while an EF write command is still open.
/// Silently removing MARS from any config file would cause sporadic "There is already an
/// open DataReader associated with this Connection" failures under the shared-connection path.
///
/// This test parses config files that are committed to the repo. Production appsettings are
/// not committed and are assumed to match (ops validated separately).
/// </summary>
public sealed class MarsConnectionStringGuardTests
{
    // Matches the "Default" conn-string value in appsettings JSON.
    // The value is scoped to SQL Server connection strings by requiring "Server=" at the start
    // of the value — prevents false-positive matches on Logging.LogLevel.Default etc.
    private static readonly Regex JsonDefaultConnPattern = new(
        @"""Default""\s*:\s*""(Server=[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Source files to assert — relative to repo root. Production appsettings not in repo;
    // omit rather than fail on missing (ops environment owns prod config).
    //
    // docker-compose.yml is intentionally omitted: the compose file has TWO service entries —
    // authserver (which does not run cross-module transactions and has no MARS) and api (which
    // does). The authoritative per-host settings are the appsettings JSON files below; those
    // are the configs loaded by the running process and cover both hosts correctly.
    private static readonly (string RelativePath, Regex Pattern)[] CheckedFiles =
    [
        (Path.Combine("src", "host", "AqtCoreFW.Api", "appsettings.Development.json"), JsonDefaultConnPattern),
        (Path.Combine("src", "host", "AqtCoreFW.Api", "appsettings.Benchmark.json"),   JsonDefaultConnPattern),
        (Path.Combine("src", "host", "AqtCoreFW.AuthServer", "appsettings.json"),       JsonDefaultConnPattern),
    ];

    [Fact]
    public void AllCommittedConnectionStrings_MustHave_MultipleActiveResultSets_True()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var (relativePath, pattern) in CheckedFiles)
        {
            var fullPath = Path.Combine(repoRoot, relativePath);

            // Skip files that do not exist in this checkout (e.g. env-specific files not committed).
            if (!File.Exists(fullPath)) continue;

            var text = File.ReadAllText(fullPath);
            var matches = pattern.Matches(text);

            if (matches.Count == 0)
            {
                violations.Add($"{relativePath}: no Default connection string found (pattern did not match)");
                continue;
            }

            foreach (Match m in matches)
            {
                var connStr = m.Groups[1].Value;
                if (!connStr.Contains("MultipleActiveResultSets=true", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{relativePath}: Default connection string missing MultipleActiveResultSets=true. " +
                        $"Found: {connStr[..Math.Min(connStr.Length, 120)]}");
                }
            }
        }

        violations.Should().BeEmpty(
            because: "the ambient cross-module unit of work shares one SqlConnection across multiple " +
                     "DbContexts; MARS (MultipleActiveResultSets=true) is required to allow interleaved " +
                     "result sets on that shared connection. Removing it causes sporadic DataReader " +
                     "conflicts under the shared-connection path. Files checked: " +
                     string.Join(", ", CheckedFiles.Select(f => f.RelativePath)));
    }

    /// <summary>
    /// Walks up from the test binary location until it finds a directory containing both
    /// <c>src/</c> and <c>tests/</c>. Mirrors the helper in <see cref="SecretConfigBypassGuardTests"/>.
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
        dir.Should().NotBeNull(
            $"could not locate repo root walking up from {AppContext.BaseDirectory} (expected src/ + tests/)");
        return dir!.FullName;
    }
}
