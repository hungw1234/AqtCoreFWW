using System.Text.RegularExpressions;
using AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.SharedKernel.Tests.Architecture;

/// <summary>
/// Phase-04C tier-rule architecture tests (PHASE4C-ARCH backlog item).
///
/// Hosts forward-looking guards against tier-discipline drift:
///   R_INTERNAL_CTRL : controllers must NOT call repository <c>*InternalAsync</c> methods
///                     (long-Id internal pre-resolve must not leak past HTTP boundary).
///   R_T2_REPO_SIG   : T2 repository interfaces must NOT expose single-overload
///                     <c>GetByIdAsync(long)</c> — convention is <c>GetByPublicIdAsync(Guid)</c>
///                     (external) + optional <c>GetByInternalIdAsync(long)</c> (Phase-4b sibling).
///   Coverage fact   : every CSV T2 entity has a discoverable repo interface OR is on
///                     the documented exception list (DbContext-direct / aggregate-owned).
///
/// Sibling rules live in <see cref="SourceLevelRulesTests"/> (R1/R3/R5/R_T2FK) and
/// <see cref="IdentifierTierShapeRulesTests"/> (Rule6/7/8 entity-shape contracts).
/// </summary>
[Trait("Category", "Architecture")]
public sealed class TierRepoArchitectureRulesTests
{
    private readonly T2EntityRegistry _registry = T2EntityRegistry.Load();

    // ─────────────────────────────────────────────────────────────────────────
    // R_INTERNAL_CTRL — Phase-02
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bans controller methods from invoking repository <c>*InternalAsync</c> APIs.
    ///
    /// <para>Drift this prevents (forward-looking, Phase-4b sibling):</para>
    /// <para>Phase-4b will add <c>GetByInternalIdAsync(long)</c> on T2 repos as the internal
    /// pre-resolve accessor (used by command handlers that already hold a long Id from a prior
    /// query). Calling it from controller actions = leaking long internal Id past the HTTP
    /// external boundary, undermining the PublicId AK contract.</para>
    ///
    /// <para>Suppress directive (per-line escape hatch):</para>
    /// <code>// arch:suppress R_INTERNAL_CTRL &lt;MethodName&gt;: &lt;reason ≥3 chars&gt;</code>
    /// <para>The "entity" token here is the method name spelled exactly as called
    /// (e.g. <c>GetByInternalIdAsync</c>) — adapted from R_T2FK's entity-based pattern
    /// because <c>*InternalAsync</c> suffix is the lexical anchor, not a CSV-known entity.</para>
    /// </summary>
    [Fact]
    public void Rule_InternalAsync_NotCalled_From_Controllers()
    {
        // Two regexes by design: scanner uses bare `\.\w+InternalAsync\s*\(` for line-match
        // detection; the extractor adds a capture group on the method-name token so the suppress
        // directive can match by exact method spelling (entity = method name).
        var methodNameRx = new Regex(@"\.(\w+InternalAsync)\s*\(", RegexOptions.Compiled);
        Func<string, string?> methodNameExtractor =
            line => methodNameRx.Match(line) is { Success: true } m ? m.Groups[1].Value : null;

        var violations = SourceCodeScanner
            .ScanForPattern(@"\.\w+InternalAsync\s*\(", ruleCode: "R_INTERNAL_CTRL", methodNameExtractor)
            .Where(v => IsControllerFile(v.FilePath))
            .ToList();

        violations.Should().BeEmpty(
            "Controllers must NOT call repository *InternalAsync methods — those bypass the " +
            "PublicId external boundary and leak long internal Id onto the HTTP surface. " +
            "Use GetByPublicIdAsync(Guid) on the controller surface; resolve long Id in the " +
            "Application layer (command/query handler). Suppress with " +
            "`// arch:suppress R_INTERNAL_CTRL <MethodName>: <reason>` if a debugging or admin " +
            "endpoint legitimately requires internal-Id access. Violations:\n" +
            string.Join("\n", violations.Select(v => $"  {v.FilePath}:{v.LineNumber} → {v.Line}")));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R_T2_REPO_SIG — Phase-03
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// T2 entity repositories MUST NOT declare single-overload <c>GetByIdAsync(long)</c>.
    ///
    /// <para>Convention (PR-C 2026-05-07, commit <c>5a70062f</c>):</para>
    /// <list type="bullet">
    /// <item>External lookup → <c>GetByPublicIdAsync(Guid publicId, ...)</c></item>
    /// <item>Internal pre-resolve (when needed) → <c>GetByInternalIdAsync(long id, ...)</c>
    /// (Phase-4b sibling — paired with the R_INTERNAL_CTRL controller-ban above)</item>
    /// </list>
    ///
    /// <para>T1 entities are exempt — single-overload <c>GetByIdAsync(long)</c> is the legitimate
    /// pattern (long Id IS the addressing key for T1; no PublicId AK exists). Phase-01 verified
    /// 14 existing T1 callsites; rule is entity-aware filtered via CSV tier registry.</para>
    ///
    /// <para>ASP.NET Identity Guid-PK satellites (ApplicationRole etc.) are also exempt — see
    /// <see cref="AspNetIdentityGuidPkExceptions"/>. Their <c>GetByIdAsync(Guid)</c> already uses
    /// the AK type; the rule pattern matches only <c>(long)</c> overloads.</para>
    ///
    /// <para>v1 ships WITHOUT suppress directive (KISS — 0 expected violations on shipped codebase
    /// per PR-C). If Phase-4b sinh edge case, extend SourceCodeScanner's entityExtractor signature
    /// to accept (filePath, line) so directive can use entity-name from filename; refactor ~1h.</para>
    /// </summary>
    [Fact]
    public void Rule_T2_Repository_Interface_MustNot_Expose_GetByIdAsync_Long()
    {
        var t2Names = _registry.T2().Select(e => e.Entity).ToHashSet(StringComparer.Ordinal);
        if (t2Names.Count == 0) return; // trivial pass — CSV not loaded yet

        // Pattern: matches `Task<X?> GetByIdAsync(long ...)` style declarations on interface bodies.
        // Whitespace tolerant; matches up through opening `long` parameter type only.
        var pattern = @"\bTask<\w+\??>\s+GetByIdAsync\s*\(\s*long\b";

        // No suppress directive in v1 (KISS) — pass null/null per backwards-compat overload.
        var sep = Path.DirectorySeparatorChar;
        var violations = SourceCodeScanner
            .ScanForPattern(pattern)
            .Where(v => v.FilePath.Contains($"{sep}src{sep}modules{sep}", StringComparison.OrdinalIgnoreCase))
            .Where(v => IsRepositoryInterfaceFile(v.FilePath))
            .Where(v =>
            {
                var entity = EntityNameFromRepoFile(v.FilePath);
                if (entity is null) return false;
                if (AspNetIdentityGuidPkExceptions.Contains(entity)) return false;
                return t2Names.Contains(entity);
            })
            .ToList();

        violations.Should().BeEmpty(
            "T2 repository interfaces must NOT expose single-overload GetByIdAsync(long ...). " +
            "Convention (PR-C 2026-05-07): T2 repos use GetByPublicIdAsync(Guid) for external " +
            "lookup; if internal long-Id resolution required, add GetByInternalIdAsync(long) " +
            "(Phase-4b sibling). Single-overload GetByIdAsync(long) is reserved for T1 entities " +
            "where long Id IS the addressing key. Violations:\n" +
            string.Join("\n", violations.Select(v =>
                $"  {v.FilePath}:{v.LineNumber} → {v.Line}  [entity={EntityNameFromRepoFile(v.FilePath)}]")));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Coverage fact — Phase-04
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every CSV T2 entity must have a discoverable repository interface file OR be on the
    /// documented exception list (DbContext-direct or aggregate-owned).
    ///
    /// <para>Defends against silent-pass via repo file naming drift: if a future T2 entity is
    /// added without a matching repo, R_T2_REPO_SIG would never scan it. Coverage fact failure
    /// forces explicit decision — add the repo (rules apply) OR add to exception list with a
    /// rationale comment.</para>
    ///
    /// <para>Maintenance protocol: when retiering an entity to/from T2, update either
    /// (a) the repo file path, OR (b) <see cref="T2NoRepoExceptions"/>, OR (c) <see cref="T2RenamedRepoMap"/>.</para>
    /// </summary>
    [Fact]
    public void CoverageFact_T2_Entities_Have_Discoverable_Repository_File()
    {
        var t2Entities = _registry.T2().Select(e => e.Entity).ToList();
        if (t2Entities.Count == 0) return; // trivial pass — CSV not loaded

        var root = SourceCodeScanner.LocateRepoRoot();
        var modulesRoot = Path.Combine(root, "src", "modules");
        if (!Directory.Exists(modulesRoot))
        {
            // Test environment without source tree — skip rather than false-fail.
            return;
        }

        // Single filesystem walk: build a HashSet of all I*Repository.cs file basenames
        // (case-insensitive). Avoids 30 walks (one per T2 entity).
        var allRepoFiles = Directory
            .EnumerateFiles(modulesRoot, "I*Repository.cs", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var entity in t2Entities)
        {
            // Identity Guid-PK satellites — repo file may have non-conventional name; skip.
            if (AspNetIdentityGuidPkExceptions.Contains(entity)) continue;

            // Documented exception — DbContext-direct or aggregate-owned, no dedicated repo.
            if (T2NoRepoExceptions.Contains(entity)) continue;

            // Conventional naming: `I{Entity}Repository.cs`.
            var conventional = $"I{entity}Repository.cs";
            if (allRepoFiles.Contains(conventional)) continue;

            // Documented rename mapping (e.g., FileRecord → IFileRepository.cs).
            if (T2RenamedRepoMap.TryGetValue(entity, out var renamed)
                && allRepoFiles.Contains(renamed))
                continue;

            missing.Add(entity);
        }

        missing.Should().BeEmpty(
            "Every T2 entity must have either (a) a discoverable repo interface " +
            "`I{Entity}Repository.cs`, (b) an entry in T2RenamedRepoMap if the file uses a " +
            "non-conventional name, OR (c) an entry in T2NoRepoExceptions if the entity is " +
            "managed via DbContext directly or owned by a parent aggregate. Missing entities " +
            "(neither found nor exempted):\n  " + string.Join("\n  ", missing));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R_T2_PUBLICID_REQUIRED — Phase-04C FU3 (positive coverage)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// T2 entity repositories MUST expose an external Guid lookup — either by direct
    /// <c>GetByPublicIdAsync(Guid)</c> declaration, or by inheriting
    /// <c>IRepository&lt;T, Guid&gt;</c> / <c>IRepository&lt;T, GuidWrapper&gt;</c>
    /// (Guid-backed strongly-typed Id like <c>FileId</c>).
    ///
    /// <para>Closes the Phase-4b regression vector left open by R_T2_REPO_SIG: the negative
    /// rule bans <c>GetByIdAsync(long)</c> on T2 repos, but a T2 repo could ship with NO
    /// external accessor at all (only internal/specialized methods) and silently pass.
    /// This positive rule asserts the canonical external addressing path exists.</para>
    ///
    /// <para>Two acceptable patterns (per <c>docs/code-standards.md</c> "T2 Tier — Repository
    /// Naming Convention"):</para>
    /// <list type="bullet">
    /// <item>Pattern A (preferred): explicit <c>Task&lt;T?&gt; GetByPublicIdAsync(Guid publicId, ...)</c></item>
    /// <item>Pattern B (acceptable): <c>: IRepository&lt;T, Guid&gt;</c> or
    /// <c>: IRepository&lt;T, FileId&gt;</c> — inherits <c>GetByIdAsync(Guid)</c> via the
    /// generic base, semantically equivalent for T2 since the AK type IS Guid.</item>
    /// </list>
    ///
    /// <para>Same exemption set as R_T2_REPO_SIG: AspNetIdentityGuidPkExceptions (Guid-PK by
    /// design) and T2NoRepoExceptions (no dedicated repo file).</para>
    /// </summary>
    [Fact]
    public void Rule_T2_Repository_Must_Declare_External_Guid_Accessor()
    {
        var t2Entities = _registry.T2().Select(e => e.Entity).ToList();
        if (t2Entities.Count == 0) return; // trivial pass — CSV not loaded

        var root = SourceCodeScanner.LocateRepoRoot();
        var modulesRoot = Path.Combine(root, "src", "modules");
        if (!Directory.Exists(modulesRoot)) return;

        // Build basename → fullpath map for O(1) lookup.
        var repoFilesByName = Directory
            .EnumerateFiles(modulesRoot, "I*Repository.cs", SearchOption.AllDirectories)
            .GroupBy(p => Path.GetFileName(p)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Pattern A: explicit `GetByPublicIdAsync(Guid` declaration anywhere in file.
        var publicIdMethodRx = new Regex(@"\bGetByPublicIdAsync\s*\(\s*Guid\b", RegexOptions.Compiled);

        // Pattern B: inherits `IRepository<TEntity, Guid>` or `IRepository<TEntity, *Id>`
        // (Guid-backed strongly-typed wrapper). Matches multi-line interface declarations.
        var genericBaseRx = new Regex(
            @":\s*IRepository<\s*\w+\s*,\s*(Guid|\w+Id)\s*>",
            RegexOptions.Compiled);

        var missing = new List<(string Entity, string FilePath)>();
        foreach (var entity in t2Entities)
        {
            if (AspNetIdentityGuidPkExceptions.Contains(entity)) continue;
            if (T2NoRepoExceptions.Contains(entity)) continue;

            var basename = T2RenamedRepoMap.TryGetValue(entity, out var renamed)
                ? renamed
                : $"I{entity}Repository.cs";

            if (!repoFilesByName.TryGetValue(basename, out var filePath))
                continue; // coverage fact owns the "missing repo" assertion; don't double-fail

            var content = File.ReadAllText(filePath);
            var hasPatternA = publicIdMethodRx.IsMatch(content);
            var hasPatternB = genericBaseRx.IsMatch(content);

            if (!hasPatternA && !hasPatternB)
                missing.Add((entity, filePath));
        }

        missing.Should().BeEmpty(
            "Every T2 repository interface must expose an external Guid lookup — either " +
            "(A) explicit `GetByPublicIdAsync(Guid publicId, ...)`, or (B) inherit " +
            "`IRepository<T, Guid>` / `IRepository<T, GuidWrapperId>` (Guid-backed strong type). " +
            "T2 entities lacking both patterns leave commands/queries with no canonical external " +
            "accessor, breaking the PublicId AK contract. Add Pattern A method or extend the " +
            "generic base. Missing entities:\n" +
            string.Join("\n", missing.Select(m => $"  {m.Entity} → {m.FilePath}")));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers + exception data
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsControllerFile(string filePath)
    {
        var sep = Path.DirectorySeparatorChar;
        // Match files ending with Controller.cs under any /Controllers/ folder.
        // Excludes test mocks (already filtered by scanner whitelist) and base classes.
        return filePath.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
            && filePath.Contains($"{sep}Controllers{sep}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRepositoryInterfaceFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.StartsWith("I", StringComparison.Ordinal)
            && fileName.EndsWith("Repository.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Derive entity name from an <c>I{Entity}Repository.cs</c> filename.
    /// Returns null if filename doesn't match the conventional pattern.
    /// E.g. <c>INotificationTemplateRepository.cs</c> → <c>NotificationTemplate</c>.
    /// </summary>
    private static string? EntityNameFromRepoFile(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (!name.StartsWith("I", StringComparison.Ordinal)) return null;
        if (!name.EndsWith("Repository", StringComparison.Ordinal)) return null;
        return name[1..^"Repository".Length];
    }

    /// <summary>
    /// ASP.NET Identity Guid-PK cluster — repos use <c>GetByIdAsync(Guid)</c> by design
    /// (OpenIddict framework lock; Guid IS the AK). Mirrors the same list in
    /// <see cref="IdentifierTierShapeRulesTests"/>.
    /// </summary>
    private static readonly HashSet<string> AspNetIdentityGuidPkExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationUser",
        "ApplicationRole",
        "GroupRoleAssignment",
        "RoleDataScope",
        "RolePermission",
        "MenuRolePermission",
        "Permission",
        "UserGroupMembership",
    };

    /// <summary>
    /// T2 entities legitimately without a dedicated repository interface — managed via
    /// DbContext directly or owned by a parent aggregate. Phase-01 coverage research
    /// (<c>plans/260507-1652-phase-04c-arch-tier-rules/research/t2-repo-coverage-260507.md</c>)
    /// catalogued these per-entity rationale.
    /// </summary>
    private static readonly HashSet<string> T2NoRepoExceptions = new(StringComparer.Ordinal)
    {
        // masterdata — DbContext direct (lookup-style aggregates with no per-entity invariant)
        "Dictionary",
        "DictionaryItem",
        "ExternalMapping",
        "WorkflowActionTaxonomy",
        "WorkflowStateTaxonomy",
        // identity — DbContext direct
        "AppMenu",
        "UserGroup",
        // organization — DbContext direct (tree access pattern)
        "OrgUnit",
        // rules — owned by RuleSet aggregate
        "Rule",
    };

    /// <summary>
    /// T2 entities whose repo interface file uses a non-conventional name (entity → repo-file
    /// basename). Maintain when adding a repo with a shortened or grouped name.
    /// </summary>
    private static readonly Dictionary<string, string> T2RenamedRepoMap = new(StringComparer.Ordinal)
    {
        ["FileRecord"] = "IFileRepository.cs",                  // Files: aggregate root short-name
        ["AccessReviewRecord"] = "IAccessReviewRepository.cs",  // Identity: dropped Record suffix
        ["ApplicationRole"] = "IRoleRepository.cs",             // Identity: ASP.NET Identity short-name
        ["ConsentRecord"] = "IConsentRepository.cs",            // Identity: dropped Record suffix
        ["UserDelegation"] = "IDelegationRepository.cs",        // Identity: dropped User prefix
    };
}
