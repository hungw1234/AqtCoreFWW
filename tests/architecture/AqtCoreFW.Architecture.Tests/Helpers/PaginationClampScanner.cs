using System.Text.RegularExpressions;

namespace AqtCoreFW.Architecture.Tests.Helpers;

/// <summary>
/// Source-text scanner that flags query handlers reading <c>request.PageSize</c>
/// (or equivalent parameter alias) without any defense-in-depth clamp. Enforces
/// the ARCH-FU2 rule: every new paginated handler must either:
/// <list type="bullet">
///   <item><description>Reference <c>Math.Clamp</c> on PageSize (bounds both directions).</description></item>
///   <item><description>Use the <c>PaginationDefaults</c> helper.</description></item>
///   <item><description>Ship with a FluentValidation <c>InclusiveBetween(1, max)</c> rule on PageSize.</description></item>
/// </list>
/// <c>Math.Min</c> is intentionally NOT accepted (ARCH-FU2.1): it caps the upper bound but
/// silently passes PageSize&lt;=0 through to the SQL OFFSET/FETCH, producing empty pages
/// instead of a 4xx — a partial defense. Use <c>Math.Clamp(_, 1, max)</c> instead.
///
/// Existing unclamped handlers are grandfathered via <see cref="GrandfatheredFiles"/>.
/// New entries to that allowlist require explicit code-review approval — the list
/// is intentionally a forcing function that shrinks each module-by-module sweep FU.
///
/// Why source-text and not Roslyn: clamp idioms live as compile-time expressions
/// that survive a substring search; heavy Roslyn integration is unjustified at
/// per-file scan cost. Mirrors <c>CrossSchemaSqlScanner</c> design.
/// </summary>
public static class PaginationClampScanner
{
    /// <summary>
    /// Handlers known to read PageSize without inline clamp at the time ARCH-FU2 shipped.
    /// Each entry is a project-relative path under <c>src/</c>. NEW additions require
    /// reviewer justification; preferred path is to FIX the handler instead.
    ///
    /// Sweep program complete 2026-05-17 v2.137p49 (ARCH-FU2.6) — allowlist empty.
    /// Carry-over FUs (FU2.2 audit → FU2.5+ bundled → FU2.6 rules) swept all 31 handlers
    /// across 11 modules; 28 use <c>AdminMax=100</c>, 3 use <c>PickerMax=1000</c>
    /// (workflow taxonomies + rule-sets — all FE bulk-picker / client-side-search semantics).
    /// </summary>
    public static readonly IReadOnlySet<string> GrandfatheredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // ── audit module ─── SWEPT 2026-05-17 v2.137p45 (ARCH-FU2.2) — all 5 handlers
        //                     migrated to PaginationDefaults.Normalize(AdminMax=100).

        // ── identity module ─── SWEPT 2026-05-17 v2.137p46 (ARCH-FU2.3) — all 7 handlers
        //                       migrated to PaginationDefaults.Normalize(AdminMax=100).
        //                       FE pre-scout: all identity hooks pass pageSize<=100.

        // ── forms / notifications / files / integration / collaboration / reporting / ai ───
        //    SWEPT 2026-05-17 v2.137p48 (ARCH-FU2.5+ bundled) — all 13 handlers across 7
        //    modules migrated to PaginationDefaults.Normalize(AdminMax=100). FE pre-scout:
        //    all callers pass pageSize<=100. Two collaboration cursor handlers
        //    (GetMessages, SearchMessages) use Math.Clamp(_, 1, AdminMax) directly because
        //    queries have no Page parameter (cursor-based via Before/AfterMessageId).

        // ── rules module ─── SWEPT 2026-05-17 v2.137p49 (ARCH-FU2.6) — ListRuleSets migrated to
        //                     PaginationDefaults.PickerMax (1000). Coordinated FE change in same
        //                     FU dropped useRuleSets({pageSize:9999}) to {pageSize:1000}.

        // ── masterdata module ─── SWEPT 2026-05-17 v2.137p47 (ARCH-FU2.4) — all 5 handlers
        //                          migrated. Dictionaries/Items/ExternalMappings use
        //                          PaginationDefaults.AdminMax (100, FE callers <=30).
        //                          WorkflowAction/StateTaxonomyQueries use PaginationDefaults.PickerMax
        //                          (1000) — FE useWorkflowStateTaxonomies/useWorkflowActionTaxonomies
        //                          pass default pageSize=200 (full-set bulk picker semantics).

        // ── ALL MODULES SWEPT — ARCH-FU2.x program complete 2026-05-17 v2.137p49 ───────────
        //    Allowlist deliberately retained empty as a forcing function: any new paginated
        //    handler reading request.PageSize without a defense token (Math.Clamp /
        //    PaginationDefaults / FluentValidation InclusiveBetween) fails the build.
    };

    /// <summary>
    /// Matches handler-entry reads of <c>PageSize</c> via the canonical parameter aliases.
    /// The pattern is intentionally narrow to avoid matching DTO property declarations,
    /// FluentValidation rule lambdas (<c>x => x.PageSize</c>), or Dapper param assignments.
    /// </summary>
    private static readonly Regex PageSizeReadPattern = new(
        @"\b(request|query|req|q|cmd|filter)\.PageSize\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Any one of these substrings in the same file counts as a valid defense.
    /// <c>Math.Min</c> was dropped in ARCH-FU2.1 — it caps only the upper bound;
    /// negative/zero PageSize would still flow into SQL. Use <c>Math.Clamp(_, 1, max)</c>.
    /// </summary>
    private static readonly string[] DefenseTokens =
    [
        "Math.Clamp",
        "PaginationDefaults",
        "InclusiveBetween(1,",
        "InclusiveBetween(1 ,", // tolerate spacing
    ];

    public sealed record Violation(string RelativePath, int LineNumber, string Snippet);

    /// <summary>
    /// Scan <paramref name="srcRoot"/> recursively. Returns one violation per file
    /// that reads <c>request.PageSize</c> (or alias) without any defense token, and
    /// is not on <see cref="GrandfatheredFiles"/>.
    /// </summary>
    public static IReadOnlyCollection<Violation> Scan(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(srcRoot))
            throw new DirectoryNotFoundException($"src not found at {srcRoot}");

        var sep = Path.DirectorySeparatorChar;
        var violations = new List<Violation>();
        foreach (var csFile in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (csFile.Contains($"{sep}Migrations{sep}", StringComparison.OrdinalIgnoreCase))
                continue;
            if (csFile.Contains($"{sep}bin{sep}", StringComparison.OrdinalIgnoreCase) ||
                csFile.Contains($"{sep}obj{sep}", StringComparison.OrdinalIgnoreCase))
                continue;

            // Scope: query/handler layer. Controllers forward to handlers; commands
            // that reference PageSize (e.g. ViewDefinition.PageSize as a config field)
            // are not pagination math. Skip both — the rule targets the handler that
            // actually composes SQL with PageSize.
            if (csFile.Contains($"{sep}Controllers{sep}", StringComparison.OrdinalIgnoreCase))
                continue;
            if (csFile.Contains($"{sep}Commands{sep}", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = File.ReadAllText(csFile);
            var firstMatch = PageSizeReadPattern.Match(content);
            if (!firstMatch.Success) continue;

            // Compliant if any defense token appears anywhere in the file (handler + validator
            // commonly co-located in the same .cs file).
            if (DefenseTokens.Any(token => content.Contains(token, StringComparison.Ordinal)))
                continue;

            var relative = Path.GetRelativePath(repoRoot, csFile);
            if (GrandfatheredFiles.Contains(relative)) continue;

            var lineNumber = content[..firstMatch.Index].Count(c => c == '\n') + 1;
            var line = content.Split('\n')[lineNumber - 1].Trim();
            violations.Add(new Violation(relative, lineNumber, line));
        }

        return violations;
    }
}
