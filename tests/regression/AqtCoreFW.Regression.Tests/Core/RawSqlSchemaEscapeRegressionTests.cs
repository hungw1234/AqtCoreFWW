using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Regression.Tests.Core;

/// <summary>
/// Regression test for v2.133p4 (2026-04-28) — TC-REG-CORE-005.
/// Ensures Dapper raw SQL never references T-SQL reserved-keyword schemas without
/// bracket-escaping. Root incident: ListTenantsQueryHandler used `FROM identity.AspNetUsers`
/// which SQL Server parsed as the IDENTITY keyword — produced 500 "Incorrect syntax".
///
/// Detects: `FROM identity.X`, `JOIN identity.X`, `INTO identity.X`, `UPDATE identity.X`
/// Allows:  `FROM [identity].X` (bracket-escaped)
/// Skips:   single-line `//` and `///` doc comments, `*` block-comment continuation lines.
/// </summary>
public sealed class RawSqlSchemaEscapeRegressionTests
{
    /// <summary>T-SQL reserved keywords also used as schema names in this codebase.</summary>
    private static readonly string[] ReservedSchemas = ["identity"];

    /// <summary>Match raw SQL referring to a reserved schema directly (no `[`).</summary>
    private static readonly Regex Pattern = new(
        @"(?i)\b(FROM|JOIN|INTO|UPDATE)\s+(identity)\.",
        RegexOptions.Compiled);

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public void RawSqlMustEscapeReservedKeywordSchemas()
    {
        var srcDir = Path.Combine(FindRepoRoot(), "src");
        Directory.Exists(srcDir).Should().BeTrue($"src dir expected at {srcDir}");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            // Self-skip: this regression test contains the bad pattern as documentation strings.
            if (file.EndsWith("RawSqlSchemaEscapeRegressionTests.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip XML doc comments, single-line comments, block-comment body lines.
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*"))
                    continue;

                if (ReservedSchemas.All(s => !line.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (Pattern.IsMatch(line))
                    violations.Add($"{file}:{i + 1}: {line.Trim()}");
            }
        }

        violations.Should().BeEmpty(
            "raw SQL must escape T-SQL reserved-keyword schemas with brackets — " +
            "use `FROM [identity].[Table]` not `FROM identity.Table`. " +
            "Violations:\n" + string.Join('\n', violations));
    }

    /// <summary>
    /// Walk up from test runtime dir until we find the worktree root (the directory
    /// whose `src/` subfolder contains the solution `.slnx`). The repo layout is:
    /// `&lt;worktree&gt;/src/AqtCoreFW.slnx` — we return `&lt;worktree&gt;` so the test can
    /// `Path.Combine(repoRoot, "src")` and scan production code there.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var srcDir = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(srcDir) &&
                (Directory.GetFiles(srcDir, "*.slnx").Length > 0 ||
                 Directory.GetFiles(srcDir, "*.sln").Length > 0))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (worktree containing src/*.slnx) not found");
    }
}
