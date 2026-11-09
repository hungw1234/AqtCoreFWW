using System.Text.RegularExpressions;
using AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.SharedKernel.Tests.Architecture;

/// <summary>
/// Source-level architecture rules — operate on .cs file text directly.
/// Phase-3 (2026-05-03): scope = allowlisted modules + always-enforced foundation files.
/// Allowlist starts empty (Phase-3 ship: trivial pass on modules); Phase-4 batches expand it.
/// Foundation files (V7GuidGenerator, IGuidGenerator, Entity) are always enforced.
///
/// Rules implemented:
///   R1: Direct Guid.NewGuid() / Guid.CreateVersion7() banned outside V7GuidGenerator + Migrations + tests
///   R3: T3 entities must NOT call MarkDeleted() / Delete() — append-only invariant
///   R5: Entity.CreatedAt + Entity.UpdatedAt must NOT use DateTimeOffset (datetime2(3) universal)
///   R_T2FK: T2 entities must NOT leak `long {Name}Id` on Application external surface (DTOs/Commands/Queries) — Guid PublicId AK only
///   R_PublicMeta: WithMetadata("PublicMetadata", …) value must carry inline (IReadOnlyDictionary&lt;string, object?&gt;) cast — invariant TValue wire-contract (enforced solution-wide, no allowlist gate)
/// </summary>
[Trait("Category", "Architecture")]
public sealed class SourceLevelRulesTests
{
    private readonly AllowlistRegistry _allowlist = AllowlistRegistry.Load();
    private readonly T2EntityRegistry _registry = T2EntityRegistry.Load();

    [Fact]
    public void Rule1_GuidNewGuid_DirectCall_Banned_In_AllowlistedModules()
    {
        // Catch-list 1: enforce IGuidGenerator factory pattern; ban Guid.NewGuid() / Guid.CreateVersion7() raw calls.
        var violations = SourceCodeScanner.ScanForPattern(@"\bGuid\.(NewGuid|CreateVersion7)\s*\(");
        var enforced = FilterByAllowlist(violations);

        enforced.Should().BeEmpty(
            "Allowlisted modules must use IGuidGenerator (NewIdMonotonic/NewIdRandom) instead of Guid.NewGuid()/CreateVersion7(). " +
            $"Allowlist size: modules={_allowlist.Modules.Count}, entities={_allowlist.Entities.Count}. " +
            "Violations:\n" + string.Join("\n", enforced.Select(v => $"  {v.FilePath}:{v.LineNumber} → {v.Line}")));
    }

    [Fact]
    public void Rule3_T3Entities_MustNotCall_MarkDeleted_Or_Delete()
    {
        // Catch-list 3: T3 = append-only audit/event logs. Soft-delete on T3 = audit hiding attack vector.
        var t3EntityNames = _registry.T3().Select(e => e.Entity).ToHashSet();
        if (t3EntityNames.Count == 0) return; // no T3 entities classified yet → trivial pass

        var allViolations = SourceCodeScanner.ScanForPattern(@"\.(MarkDeleted|Delete)\s*\(\s*\)");
        var t3Violations = allViolations
            .Where(v => t3EntityNames.Any(name => v.FilePath.Contains(name + ".cs")))
            .Where(v => IsAllowlisted(v.FilePath))
            .ToList();

        t3Violations.Should().BeEmpty(
            "T3 (append-only) entities must not invoke MarkDeleted() or Delete(). " +
            $"T3 entities: {string.Join(", ", t3EntityNames)}. " +
            "Violations:\n" + string.Join("\n", t3Violations.Select(v => $"  {v.FilePath}:{v.LineNumber} → {v.Line}")));
    }

    [Fact]
    public void Rule5_Module_Entities_CreatedAt_UpdatedAt_MustNotBe_DateTimeOffset()
    {
        // Catch-list 5: Phase-2 H1 amendment — CreatedAt/UpdatedAt must be DateTime (datetime2(3)) on Entity-derived classes.
        // Source-level check is scoped to Domain\Entities\ paths (Entity-derived). Application DTOs and wire types
        // remain DateTimeOffset (UTC instant on the wire). Foundation contract (Entity itself) enforced at IL level
        // via IdentifierTierShapeRulesTests.Entity_CreatedAt_Is_DateTime_Not_DateTimeOffset.
        var violations = SourceCodeScanner.ScanForPattern(
            @"DateTimeOffset\??\s+(CreatedAt|UpdatedAt)\s*[\{;=]");

        var moduleViolations = violations
            .Where(v => !IsFoundationFile(v.FilePath) && IsAllowlisted(v.FilePath) && IsDomainEntityFile(v.FilePath))
            .ToList();

        moduleViolations.Should().BeEmpty(
            "Allowlisted module Domain\\Entities files must use DateTime (not DateTimeOffset) for CreatedAt/UpdatedAt. " +
            "Violations:\n" + string.Join("\n", moduleViolations.Select(v => $"  {v.FilePath}:{v.LineNumber} → {v.Line}")));
    }

    // T2-FK drift backlog DRAINED by PR-D2 (2026-05-07): WorkflowDefinition/WorkflowInstance retiered
    // long → Guid PublicId on Application surface; Rule (T1 collision with workflow.DelegationRule +
    // identity.AttributeRule) annotated via arch:suppress directive. PR-D1 (94045886) shipped the
    // suppress directive; FileRecord/OrgUnit/Message/FormTemplate dropped earlier in PR-D1 after
    // annotating 8 deliberate-design lines. Rule_T2 now enforces drift across all T2 entities with
    // zero exemptions. HashSet retained empty as documentation anchor + pause valve for future drift.
    //
    // Per-line escape hatch for deliberate-design FK (T3 cross-aggregate / T1 child / Dapper row /
    // regex collision): place `// arch:suppress R_T2FK <EntityName>: <reason>` either trailing the
    // line or on the line immediately above. See SourceCodeScannerSuppressTests for grammar spec.
    private static readonly HashSet<string> T2FkDriftBacklog = new(StringComparer.Ordinal);

    [Fact]
    public void Rule_T2_External_Surface_MustNot_Leak_LongFK()
    {
        // Catch-list T2-FK: PR-C ARCH-DTO-GUARD (Phase-07).
        // T2 entities expose Guid PublicId as external AK; long internal Id MUST NOT appear as `{Name}Id`
        // parameter/property on Application surface (DTOs / Commands / Queries records).
        // Drift this prevents: PR-B fix `110a14b5` migrated MessageLogDto.PartnerId/ContractId/FormTemplateId long → Guid.
        // Scanner is line-based — regex matches field-name regardless of file/class context (covers bundled DTOs).
        // PR-D1 (2026-05-07): suppress directive `// arch:suppress R_T2FK <Entity>: <reason>` is the per-line
        // escape hatch for deliberate-design false-positives. Backlog reduced from 7 → 3 (WorkflowDefinition/WorkflowInstance/Rule).
        var t2Names = _registry.T2().Select(e => e.Entity)
            .Where(n => !T2FkDriftBacklog.Contains(n))
            .ToList();
        if (t2Names.Count == 0) return; // trivial pass

        var alternation = string.Join("|", t2Names.Select(n => Regex.Escape(n) + "Id"));
        var pattern = @"\blong\s+(" + alternation + @")\b";
        var sep = Path.DirectorySeparatorChar;
        // Application surface = `{Module}.Application{sep}{DTOs|Commands|Queries}{sep}`. Project dirs follow `*.Application` convention.
        string[] appSurfacePaths =
        [
            $".Application{sep}DTOs{sep}",
            $".Application{sep}Commands{sep}",
            $".Application{sep}Queries{sep}",
        ];

        // Entity extractor: pulls the entity name from `long {Entity}Id` match on the violation line.
        // e.g. "long FormTemplateId" → "FormTemplate". Regex captures group 1 as the name token.
        var entityExtractorRx = new Regex(@"\blong\s+(\w+)Id\b", RegexOptions.Compiled);
        Func<string, string?> entityExtractor =
            line => entityExtractorRx.Match(line) is { Success: true } m ? m.Groups[1].Value : null;

        var violations = SourceCodeScanner.ScanForPattern(pattern, ruleCode: "R_T2FK", entityExtractor)
            .Where(v => appSurfacePaths.Any(p => v.FilePath.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Where(v => IsAllowlisted(v.FilePath))
            .ToList();

        violations.Should().BeEmpty(
            "T2 entities must expose Guid PublicId on external surface (DTOs/Commands/Queries), not long internal FK. " +
            $"T2 entity count: {t2Names.Count}. " +
            "Use Guid PublicId AK in records. Violations:\n" +
            string.Join("\n", violations.Select(v => $"  {v.FilePath}:{v.LineNumber} → {v.Line}")));
    }

    // Call-site of the reserved opt-in wire key: WithMetadata("PublicMetadata", <value>).
    private static readonly Regex PublicMetadataCallRx =
        new(@"WithMetadata\s*\(\s*""PublicMetadata""\s*,", RegexOptions.Compiled);

    // The mandatory inline cast on that value: (IReadOnlyDictionary<string, object?>).
    // Note the nullable `object?` — IReadOnlyDictionary is INVARIANT in TValue, so
    // <string, object> (non-null) is a DIFFERENT, non-assignable type for the wire match.
    private static readonly Regex PublicMetadataCastRx =
        new(@"\(\s*IReadOnlyDictionary\s*<\s*string\s*,\s*object\s*\?\s*>\s*\)", RegexOptions.Compiled);

    // A source line is well-typed iff it is NOT a PublicMetadata call-site (out of scope) OR
    // it carries the inline IReadOnlyDictionary<string, object?> cast.
    private static bool PublicMetadataCallIsWellTyped(string line) =>
        !PublicMetadataCallRx.IsMatch(line) || PublicMetadataCastRx.IsMatch(line);

    [Fact]
    public void Rule_PublicMetadata_WithMetadata_Value_MustCast_To_IReadOnlyDictionary()
    {
        // The HTTP error envelope (ApiResponse.Fail) extracts the reserved "PublicMetadata" key via
        // an `is IReadOnlyDictionary<string, object?>` pattern match — the SOLE metadata key that
        // reaches the wire. IReadOnlyDictionary is invariant in TValue: passing a bare Dictionary,
        // or IReadOnlyDictionary<string, object> (non-null), fails the match SILENTLY and surfaces
        // null metadata to the client (no exception, no log). Enforce an inline cast on every
        // WithMetadata("PublicMetadata", …) call so the value type is unambiguous at the call-site.
        // Enforced solution-wide (no allowlist gate): this is a hard wire contract, not a
        // gradual-rollout convention. Inline-cast requirement keeps the check single-line robust.
        // NOTE: the cast must be INLINE on the WithMetadata call line — a correctly-typed named
        // local (e.g. `IReadOnlyDictionary<string, object?> pm = …; WithMetadata("PublicMetadata", pm)`)
        // will still trip this rule. Keep the cast at the call-site to satisfy the single-line scan.
        var callSites = SourceCodeScanner.ScanForPattern(PublicMetadataCallRx.ToString());
        var violations = callSites.Where(v => !PublicMetadataCallIsWellTyped(v.Line)).ToList();

        violations.Should().BeEmpty(
            "every WithMetadata(\"PublicMetadata\", …) value must carry an inline " +
            "(IReadOnlyDictionary<string, object?>) cast — invariant TValue; a wrong/missing cast " +
            "silently produces null metadata on the wire. Place the cast on the same line as the " +
            "WithMetadata call. Violations:\n" +
            string.Join("\n", violations.Select(v => $"  {v.FilePath}:{v.LineNumber} → {v.Line}")));
    }

    [Theory]
    // Anti-tautology guard: proves the predicate distinguishes good vs bad call-sites rather than
    // trivially passing. Without this, the [Fact] could go green forever even if the regex broke.
    [InlineData(@"error.WithMetadata(""PublicMetadata"", (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>", true)]   // canonical inline cast
    [InlineData(@"e.WithMetadata(""PublicMetadata"",(IReadOnlyDictionary<string,object?>)dict);", true)]                                  // whitespace-tight variant
    [InlineData(@"error.WithMetadata(""PublicMetadata"", new Dictionary<string, object?> { });", false)]                                  // missing cast → violation
    [InlineData(@"error.WithMetadata(""PublicMetadata"", (IReadOnlyDictionary<string, object>)dict);", false)]                            // non-null TValue → violation
    [InlineData(@"error.WithMetadata(""WorkflowErrorCode"", WorkflowErrorCodes.GraphValidationFailed);", true)]                           // different key → out of scope
    public void PublicMetadataCast_Predicate_Distinguishes_WellTyped_From_Violations(string line, bool expectedWellTyped)
    {
        PublicMetadataCallIsWellTyped(line).Should().Be(expectedWellTyped);
    }

    private static bool IsDomainEntityFile(string filePath) =>
        filePath.Contains($"{Path.DirectorySeparatorChar}Domain{Path.DirectorySeparatorChar}Entities{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Allowlist_File_Loads_Successfully()
    {
        // Smoke check: allowlist JSON parses + readable. Catches malformed JSON early.
        _allowlist.Should().NotBeNull();
    }

    [Fact]
    public void EntityTierRegistry_Loads_Successfully()
    {
        // Smoke check: CSV parses + has rows. Defends against schema drift.
        _registry.All.Should().NotBeEmpty("entity-tier-mapping-data.csv must load with at least one entity");
        _registry.T2().Should().NotBeEmpty("at least one T2 entity should be classified post-Phase-1");
    }

    private List<SourceCodeScanner.Violation> FilterByAllowlist(IReadOnlyList<SourceCodeScanner.Violation> violations) =>
        violations.Where(v => IsAllowlisted(v.FilePath)).ToList();

    private bool IsAllowlisted(string filePath)
    {
        if (_allowlist.IsEmpty) return false;
        return _allowlist.Modules.Any(m =>
            filePath.Contains($"{Path.DirectorySeparatorChar}{m}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFoundationFile(string filePath) =>
        filePath.Contains("AqtCoreFW.SharedKernel", StringComparison.OrdinalIgnoreCase) ||
        filePath.Contains("AqtCoreFW.Infrastructure", StringComparison.OrdinalIgnoreCase);
}
