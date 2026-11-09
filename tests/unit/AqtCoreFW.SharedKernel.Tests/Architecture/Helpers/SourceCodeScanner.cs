using System.Text.RegularExpressions;

namespace AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;

/// <summary>
/// Source-text regex scanner over .cs files under repository root.
/// Phase-3 (2026-05-03): replaces planned Roslyn AST approach (M1 amendment — KISS).
/// Phase-07 PR-D1 (2026-05-07): adds generic arch:suppress directive recognition.
///
/// Resolves repo root by walking up from current assembly location until directory contains AqtCoreFW.slnx.
/// Whitelist paths exclude generated code, migrations, test projects, and the V7GuidGenerator implementation.
///
/// Suppress directive grammar (per-line escape hatch for deliberate-design false-positives):
///   // arch:suppress &lt;RuleCode&gt; &lt;EntityName&gt;: &lt;reason (≥3 chars)&gt;
/// Directive may appear trailing on the same line as the violation OR on the immediately preceding line.
/// </summary>
public static class SourceCodeScanner
{
    private static readonly string[] DefaultWhitelistFragments =
    [
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
        // Seeders run once on bootstrap; ASP.NET Identity Guid PK seed values fall outside
        // domain-event MessageId discipline. Same exemption applied at IL-level rules.
        $"{Path.DirectorySeparatorChar}Seeding{Path.DirectorySeparatorChar}",
        "V7GuidGenerator.cs",
        "FakeGuidGenerator.cs",
        // FileId VO factory pattern — value-objects encapsulate identity creation by design.
        "FileId.cs",
        // FileSecurityService.GenerateSecureFilePath: random tenant-scoped storage path,
        // not a domain entity Id; collision-resistance only, no monotonicity requirement.
        "FileSecurityService.cs",
        // TableFieldDataRepository: Dapper Phase-2 seed for legacy table-driven master-data;
        // p_Id is not an entity FK, just a row-level uniqueness handle.
        "TableFieldDataRepository.cs",
        // DocumentSigningWorkflow.InitiateAsync: JWT cryptographic nonce
        // (jwtNonce field on SigningDocFlowRecord + FK to SigningDocFlowNonce row).
        // Random-collision resistance only; no monotonicity or sortable-Id semantics required.
        // Mirrors FileSecurityService.cs precedent: cryptographic randomness ≠ entity Id.
        "DocumentSigningWorkflow.cs",
    ];

    // Suppress directive regex. Named groups: rule, entity, reason.
    // Grammar: // arch:suppress <RuleCode> <EntityName>: <reason≥3chars>
    // Forge-resistance: reason must be ≥3 non-whitespace-leading chars after the colon.
    // (?<reason>\S.{2,}) = first non-space char + at least 2 more chars (total ≥3).
    // M2 (PR-D2): negative lookbehind `(?<!/)//` — `///` doc-comments and `////` quad-slash
    // do NOT activate the directive (only standalone `//` or trailing `//` qualify).
    private static readonly Regex SuppressDirectiveRegex = new(
        @"(?<!/)//\s*arch:suppress\s+(?<rule>\S+)\s+(?<entity>\S+)\s*:\s*(?<reason>\S.{2,})",
        RegexOptions.Compiled);

    public sealed record Violation(string FilePath, int LineNumber, string Line, string Pattern);

    /// <summary>
    /// Scans source for <paramref name="regexPattern"/> matches with optional per-line suppress directive support.
    /// </summary>
    /// <param name="regexPattern">The pattern to search for in source lines.</param>
    /// <param name="ruleCode">Rule code token (e.g. "R_T2FK"). If null, suppress checking is disabled.</param>
    /// <param name="entityExtractor">
    /// Function that extracts the entity name from a matched source line.
    /// Return null if entity cannot be determined (violation will not be suppressible).
    /// Required when <paramref name="ruleCode"/> is non-null.
    /// </param>
    /// <param name="extraWhitelistFragments">Additional path fragments to whitelist.</param>
    public static IReadOnlyList<Violation> ScanForPattern(
        string regexPattern,
        string? ruleCode,
        Func<string, string?>? entityExtractor,
        IEnumerable<string>? extraWhitelistFragments = null)
    {
        var rx = new Regex(regexPattern, RegexOptions.Compiled);
        var whitelist = DefaultWhitelistFragments.Concat(extraWhitelistFragments ?? []).ToArray();
        var root = LocateRepoRoot();
        var srcRoot = Path.Combine(root, "src");
        if (!Directory.Exists(srcRoot)) return Array.Empty<Violation>();

        var violations = new List<Violation>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (whitelist.Any(w => file.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // Skip C# comment lines so literal pattern text in /// or // comments
                // (e.g. "Rule1 — no Guid.NewGuid() defaults") doesn't trigger false positives.
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;

                if (!rx.IsMatch(lines[i])) continue;

                // Check suppress directive when ruleCode + extractor are wired.
                if (ruleCode is not null && entityExtractor is not null)
                {
                    var entity = entityExtractor(lines[i]);
                    if (entity is not null)
                    {
                        var prevLine = i > 0 ? lines[i - 1] : null;
                        if (IsSuppressed(lines[i], prevLine, ruleCode, entity))
                        {
                            // M1 (PR-D2): diagnostic Console.WriteLine removed — silently swallowed
                            // by xUnit default; suppression is observable via test pass/fail without it.
                            continue;
                        }
                    }
                }

                violations.Add(new Violation(file, i + 1, lines[i].Trim(), regexPattern));
            }
        }
        return violations;
    }

    /// <summary>
    /// Backwards-compatible overload — no suppress directive check.
    /// Existing callers (R1, R3, R5) are unchanged.
    /// </summary>
    public static IReadOnlyList<Violation> ScanForPattern(
        string regexPattern,
        IEnumerable<string>? extraWhitelistFragments = null)
        => ScanForPattern(regexPattern, ruleCode: null, entityExtractor: null, extraWhitelistFragments);

    /// <summary>
    /// Determines whether a source line (or its preceding line) carries a valid suppress directive
    /// matching <paramref name="ruleCode"/> and <paramref name="entity"/>.
    /// </summary>
    /// <param name="currentLine">The matched source line.</param>
    /// <param name="prevLine">The line immediately above (null if at file top).</param>
    /// <param name="ruleCode">Expected rule code token.</param>
    /// <param name="entity">Expected entity name token.</param>
    /// <returns>True if a valid, matching suppress directive is found; false otherwise.</returns>
    public static bool IsSuppressed(string currentLine, string? prevLine, string ruleCode, string entity)
    {
        // Strategy 1: trailing comment on the same line.
        // Run regex on the full line — lookbehind `(?<!/)//` naturally rejects `///` doc-comment forms
        // even if they appear elsewhere in the line (M2 PR-D2 precision).
        if (MatchesDirective(currentLine, ruleCode, entity))
            return true;

        // Strategy 2: preceding line is a pure comment carrying the directive.
        // Skip `///` doc-comment lines explicitly (M2 PR-D2) — `(?<!/)` lookbehind alone handles
        // mid-line cases, but the explicit guard makes intent self-documenting.
        if (prevLine is not null)
        {
            var prevTrimmed = prevLine.TrimStart();
            if (prevTrimmed.StartsWith("///")) return false;
            if (prevTrimmed.StartsWith("//") && MatchesDirective(prevTrimmed, ruleCode, entity))
                return true;
        }

        return false;
    }

    // Returns true when the directive string matches the expected rule+entity with a ≥3-char reason.
    private static bool MatchesDirective(string directiveText, string ruleCode, string entity)
    {
        var m = SuppressDirectiveRegex.Match(directiveText);
        return m.Success
               && m.Groups["rule"].Value == ruleCode
               && m.Groups["entity"].Value == entity;
        // Reason length ≥3 is enforced by the regex itself (\S.{2,}).
    }

    public static string LocateRepoRoot()
    {
        // AqtCoreFW.slnx lives at src/AqtCoreFW.slnx (per repo layout); repo root contains the `src` folder + `plans` + `tests`.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "AqtCoreFW.slnx"))
                || (Directory.Exists(Path.Combine(dir.FullName, "src"))
                    && Directory.Exists(Path.Combine(dir.FullName, "plans"))
                    && Directory.Exists(Path.Combine(dir.FullName, "tests"))))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found (no ancestor contains src/AqtCoreFW.slnx + plans + tests).");
    }
}
