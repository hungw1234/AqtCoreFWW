using System.Text.RegularExpressions;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard: outside the Forms Application layer, every <c>new FormFieldDto(...)</c>
/// construction MUST use named arguments.
///
/// Why this matters: <c>FormFieldDto</c> is a positional record with 15 params (11 required + 4
/// tail-optional). Adding a NEW param at the tail is backward-safe when it has a default value
/// (Phase-07 M1 shipped <c>ParentFieldId = null</c> this way). Adding a param MID-record is NOT
/// safe for positional call sites: the compiler happily re-binds ordinal positions to the new
/// signature, so a test that used to construct <c>FormFieldDto(1L, "key", "Text", ...)</c> silently
/// starts constructing a different DTO shape - no compile error, no test-fail signal - exactly
/// the trap in <c>feedback-formtemplate-addfield-ispii-positional-trap</c> (13th late-optional
/// PII flag pushed by name-omission → PII path went dead false-green).
///
/// Application-layer handlers (<c>GetFormTemplateQueryHandler.MapField</c>,
/// <c>GetPublicFormQueryHandler</c>'s inline mapper) intentionally keep positional construction:
/// they own the DTO and any param-shape change lands next to them, so drift is obvious. Every
/// OTHER call site (tests, cross-module clients, presentation adapters) is at arm's length and
/// must use named arguments so a mid-record param insert fails LOUDLY at compile time.
///
/// Scanner: text-based (same idiom as <see cref="MessageRetryConfigGuardTests"/>). It enumerates
/// every <c>.cs</c> file under <c>src/</c> and <c>tests/</c>, excludes build output and worktree
/// clones, skips the Application-layer path, then flags any <c>new FormFieldDto(</c> whose first
/// non-whitespace argument character sequence is NOT an <c>Identifier:</c> named-arg marker.
///
/// Limitation (accepted): a call whose FIRST arg is named but subsequent args are positional
/// would pass this scanner (C# only requires named args from the first named onward, so this
/// hybrid form is syntactically legal). The realistic regression - a whole positional call site
/// - is caught in full. If a future call site adopts partial-naming and drifts, the fix is to
/// tighten the scanner (or migrate that site fully to named).
/// </summary>
public sealed class FormFieldDtoPositionalConstructionGuardTests
{
    private const string DtoName = "FormFieldDto";

    // Anchor for Application-layer allowlist (positional construction OK here - handler owns the DTO).
    private static readonly string ApplicationLayerPath =
        Path.Combine("src", "modules", "forms", "AqtCoreFW.Forms.Application")
            + Path.DirectorySeparatorChar;

    // Alt separator so the same anchor matches path strings on any platform.
    private static readonly string ApplicationLayerPathAlt =
        ApplicationLayerPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // `new FormFieldDto(` followed by an optional whitespace/newline run. Multi-line and single-line
    // constructions both fit; the captured index anchors the "first non-whitespace char after `(`" check.
    private static readonly Regex NewCallRegex = new(
        @"\bnew\s+" + DtoName + @"\s*\(",
        RegexOptions.Compiled);

    // A C# named-arg marker: `Identifier :` immediately after `(`, allowing leading whitespace/newlines.
    // Excludes `::` (namespace-qualifier) and `?:` (ternary) since they can't legally appear here as
    // the first token inside a constructor call.
    private static readonly Regex NamedArgMarkerRegex = new(
        @"^\s*[A-Za-z_][A-Za-z0-9_]*\s*:(?!:)",
        RegexOptions.Compiled);

    [Fact]
    [Trait("Category", "Architecture")]
    public void FormFieldDto_Construction_OutsideApplicationLayer_UsesNamedArguments()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(repoRoot))
        {
            var relative = Path.GetRelativePath(repoRoot, file);
            if (IsApplicationLayerPath(relative)) continue;

            // Skip THIS guard file itself: it names the DTO in its own doc-comments/regex literals
            // and would trip its own scan. (This scanner never inspects string contents anyway, so
            // the literal `new FormFieldDto(` is safe; excluding the guard file is defense-in-depth.)
            if (relative.EndsWith(nameof(FormFieldDtoPositionalConstructionGuardTests) + ".cs",
                    StringComparison.Ordinal))
                continue;

            var source = File.ReadAllText(file);
            foreach (Match match in NewCallRegex.Matches(source))
            {
                var afterOpenParen = source[(match.Index + match.Length)..];
                if (NamedArgMarkerRegex.IsMatch(afterOpenParen)) continue;

                // Compute 1-based line number for the human-readable violation report.
                var line = source[..match.Index].Count(ch => ch == '\n') + 1;
                violations.Add($"{relative}:{line}");
            }
        }

        violations.Should().BeEmpty(
            because: $"outside {ApplicationLayerPath} every `new {DtoName}(...)` MUST use named arguments " +
                     "so a future mid-record param insert fails loudly at compile time instead of silently " +
                     "shifting positional args (feedback-formtemplate-addfield-ispii-positional-trap). " +
                     $"Offending call sites:\n  - {string.Join("\n  - ", violations)}");
    }

    // ============================================================================
    // Scanner unit tests - prove the scanner CATCHES what it should and does NOT
    // flag what it shouldn't. These give the guard its own oracle so future
    // refactors of the regexes can't silently degrade to accept-all.
    // ============================================================================

    [Fact]
    [Trait("Category", "Architecture")]
    public void Scanner_DetectsPositionalConstruction_AsViolation()
    {
        const string positionalSource = """
            var f = new FormFieldDto(1L, "key", "Text", "Vi", "En", 1, true, true, 1, null, null);
            """;

        var matches = NewCallRegex.Matches(positionalSource);
        matches.Count.Should().Be(1);

        var afterOpen = positionalSource[(matches[0].Index + matches[0].Length)..];
        NamedArgMarkerRegex.IsMatch(afterOpen).Should().BeFalse(
            "the first argument `1L` is positional (no `Identifier:` marker) - scanner MUST flag this");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Scanner_AcceptsNamedArgumentConstruction()
    {
        const string namedSource = """
            var f = new FormFieldDto(
                Id: 1L,
                FieldKey: "key",
                Type: "Text",
                LabelVi: "Vi",
                LabelEn: "En",
                DisplayOrder: 1,
                Required: true,
                IsActive: true,
                AddedInVersion: 1,
                ValidationRules: null,
                Options: null);
            """;

        var matches = NewCallRegex.Matches(namedSource);
        matches.Count.Should().Be(1);

        var afterOpen = namedSource[(matches[0].Index + matches[0].Length)..];
        NamedArgMarkerRegex.IsMatch(afterOpen).Should().BeTrue(
            "the first argument is `Id: 1L` (named-arg marker) - scanner MUST accept this");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Scanner_IgnoresApplicationLayerPath()
    {
        // Both platform separators of the anchor MUST match a repo-relative path that starts with
        // the Application-layer prefix. Guards against a Windows/Unix path-separator drift.
        var winStyle = "src\\modules\\forms\\AqtCoreFW.Forms.Application\\Queries\\GetFormTemplate\\Handler.cs";
        var nixStyle = "src/modules/forms/AqtCoreFW.Forms.Application/Queries/GetFormTemplate/Handler.cs";

        IsApplicationLayerPath(winStyle).Should().BeTrue();
        IsApplicationLayerPath(nixStyle).Should().BeTrue();

        // Sibling module MUST NOT be considered Application-layer.
        IsApplicationLayerPath("src/modules/forms/AqtCoreFW.Forms.Domain/Entities/FormField.cs")
            .Should().BeFalse();
        IsApplicationLayerPath("tests/contracts/AqtCoreFW.Tests.Contracts/Api/FormsApiContractTests.cs")
            .Should().BeFalse();
    }

    // Normalizes '\' to '/' before comparing: on Linux/macOS both DirectorySeparatorChar and
    // AltDirectorySeparatorChar are '/', so the two anchors alone never match a Windows-style path.
    private static bool IsApplicationLayerPath(string relativePath) =>
        relativePath.Replace('\\', '/').StartsWith(
            ApplicationLayerPathAlt.Replace('\\', '/'), StringComparison.Ordinal);

    /// <summary>
    /// Enumerate every <c>.cs</c> file under <c>src/</c> and <c>tests/</c>, skipping build output
    /// (<c>bin/</c>, <c>obj/</c>) and Claude worktree clones (<c>.claude/worktrees/</c>) which hold
    /// snapshots of the repo that would double-count matches under their own re-scan.
    /// </summary>
    private static IEnumerable<string> EnumerateSourceFiles(string repoRoot)
    {
        foreach (var root in new[] { "src", "tests" })
        {
            var absRoot = Path.Combine(repoRoot, root);
            if (!Directory.Exists(absRoot)) continue;

            foreach (var file in Directory.EnumerateFiles(absRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (ContainsSegment(file, "bin") || ContainsSegment(file, "obj")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)) continue;
                yield return file;
            }
        }
    }

    private static bool ContainsSegment(string path, string segment)
    {
        var needle = Path.DirectorySeparatorChar + segment + Path.DirectorySeparatorChar;
        var altNeedle = Path.AltDirectorySeparatorChar + segment + Path.AltDirectorySeparatorChar;
        return path.Contains(needle, StringComparison.Ordinal)
            || path.Contains(altNeedle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the test binary location until it finds a directory containing both
    /// <c>src/</c> and <c>tests/</c>. Mirrors the helper in <see cref="MessageRetryConfigGuardTests"/>.
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
