using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Closed-allowlist guard for the audit-keyed <c>IReadDbConnection</c> reader
/// (ISO-DAPPER-KEYED-DI-INCREMENT, review finding M1).
///
/// Read-path login isolation (ISO-DAPPER) makes the DEFAULT (unkeyed) <c>IReadDbConnection</c>
/// registration run on the least-privilege NonAudit login (denied <c>[audit]</c>). A query handler
/// that legitimately needs <c>[audit]</c> opts back into the broad Default (audit-owner) login by
/// injecting the KEYED reader via <c>[FromKeyedServices(ReadConnectionKeys.Audit)]</c>.
///
/// That opt-in is exactly as safe as its allowlist: any class anywhere in <c>src/</c> could add the
/// same attribute and silently regain full <c>[audit]</c> access on the broad login, defeating the
/// isolation Phase 2/3 shipped. This guard is a SOURCE-TEXT sweep (like
/// <see cref="SecretConfigBypassGuardTests"/>/<see cref="AuditBoundaryEfWiringGuardTests"/>) over every
/// <c>.cs</c> file under <c>src/</c> — not a reflection scan — because completeness must hold for a
/// brand-new module/assembly the test project does not yet reference; a reflection sweep is only as
/// exhaustive as the assemblies it happens to load.
///
/// Expected members of the allowlist (10 — census 2026-07-14; the 2 identity SecurityHealth handlers
/// were removed the same day when they were refactored to read <c>[audit]</c> via
/// <c>IAuditEvidenceClient</c>, so identity no longer holds an <c>[audit]</c>-capable credential):
///   - 9 audit query handlers (<c>src/modules/audit/AqtCoreFW.Audit.Application/Queries/**</c>)
///   - <c>InProcessAuditEvidenceClient</c> (audit Infrastructure — the cross-module evidence adapter,
///     now also serves the identity D7 security-health reads)
/// </summary>
public sealed class AuditKeyedReaderAllowlistTests
{
    // The literal attribute usage this guard tracks. Matched only on non-comment source lines —
    // see ContainsRealAttributeUsage. Kept as a single literal (not a Regex) so the guard fails
    // loudly on any textual drift in ReadConnectionKeys.Audit's usage, not just semantic drift.
    private const string AuditKeyedAttribute = "[FromKeyedServices(ReadConnectionKeys.Audit)]";

    /// <summary>
    /// The CLOSED allowlist — repo-relative paths (from repo root) of every file permitted to inject
    /// the audit-keyed reader. Adding an 11th entry requires explicit code-review approval; this is the
    /// SOLE CI backstop for a dropped or added key.
    /// </summary>
    private static readonly string[] AllowlistedFiles =
    [
        // ── Audit module: 9 query handlers reading their own [audit] schema ────────────
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "GetAuditLogs", "GetAuditLogsQueryHandler.cs"),
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "GetAuditStatistics", "GetAuditStatisticsQueryHandler.cs"),
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "GetSecurityIncidents", "GetSecurityIncidentsQueryHandler.cs"),
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "GetRtbfRequests", "GetRtbfRequestsQueryHandler.cs"),
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "GetLoginAudit", "GetLoginAuditQueryHandler.cs"),
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "GetCompliancePolicies", "GetCompliancePoliciesQueryHandler.cs"),
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "GetClassificationRules", "GetClassificationRulesQueryHandler.cs"),
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "GetAiPromptTraces", "GetAiPromptTracesQueryHandler.cs"),
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Application", "Queries", "ExportAuditLogs", "ExportAuditLogsCsvQueryHandler.cs"),
        // ── Audit module: in-process cross-module evidence adapter (audit rollups + the identity
        //    D7 security-health reads, moved here 2026-07-14 so identity holds no [audit] credential) ─
        Path.Combine("src", "modules", "audit", "AqtCoreFW.Audit.Infrastructure", "Services", "InProcessAuditEvidenceClient.cs"),
    ];

    [Fact]
    public void ExactlyTheAllowlistedTenFiles_InjectTheAuditKeyedReader()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        Directory.Exists(srcRoot).Should().BeTrue($"src/ must exist under {repoRoot}");

        // ── Part A: every allowlisted file must still carry the attribute ──────────────
        var missingFromAllowlistedFiles = new List<string>();
        foreach (var relativePath in AllowlistedFiles)
        {
            var fullPath = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                missingFromAllowlistedFiles.Add($"{relativePath}: file not found (moved/renamed — update the allowlist).");
                continue;
            }

            if (!ContainsRealAttributeUsage(File.ReadAllText(fullPath)))
                missingFromAllowlistedFiles.Add(
                    $"{relativePath}: no longer injects {AuditKeyedAttribute} — either this handler stopped " +
                    "reading [audit] (remove it from the allowlist) or the injection regressed (restore it).");
        }

        // ── Part B: sweep every .cs under src/ — the found set must equal the allowlist exactly ──
        var allowlistSet = new HashSet<string>(AllowlistedFiles, StringComparer.OrdinalIgnoreCase);
        var foundOutsideAllowlist = new List<string>();
        var foundSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) continue;

            if (!ContainsRealAttributeUsage(File.ReadAllText(file))) continue;

            var relative = Path.GetRelativePath(repoRoot, file);
            foundSet.Add(relative);
            if (!allowlistSet.Contains(relative))
                foundOutsideAllowlist.Add(relative);
        }

        foundOutsideAllowlist.Should().BeEmpty(
            because: $"only the 10 allowlisted files may inject the audit-keyed reader ({AuditKeyedAttribute}) — " +
                     "any other class gaining this attribute silently regains broad-login [audit] access, " +
                     "defeating the NonAudit read-path isolation. Add explicit code-review approval and update " +
                     $"{nameof(AllowlistedFiles)} if this is intentional.");

        missingFromAllowlistedFiles.Should().BeEmpty(
            because: "an allowlisted handler must keep injecting the audit-keyed reader, or be removed from the allowlist");

        // Non-vacuity: the sweep must actually find exactly the 10 expected files, not silently
        // undercount (e.g. a path-separator or comment-filter bug that skips real hits) or
        // overcount (a bug that double-counts). This is the guard's own self-check.
        foundSet.Count.Should().Be(10,
            "the sweep found a different number of audit-keyed injections than the 10-file census — " +
            "either the guard's own scan logic is broken, or the allowlist is stale");
    }

    /// <summary>
    /// Proves the comment-line filter and literal-attribute match actually discriminate real code from
    /// prose — the mechanism <see cref="ExactlyTheAllowlistedTenFiles_InjectTheAuditKeyedReader"/>
    /// relies on to avoid both false positives (XML-doc mentions like the ones in
    /// <c>ReadConnectionKeys.cs</c> and <c>InfrastructureRegistration.cs</c>) and false negatives.
    ///
    /// This is the guard's mutation-proof: it exercises the SAME <see cref="ContainsRealAttributeUsage"/>
    /// helper the sweep calls, so if an 11th file added the attribute as REAL code, this positive case
    /// demonstrates the sweep would catch it (returns true → added to foundSet → fails Part B unless
    /// allowlisted); if one of the 10 lost the attribute (or kept it only in a comment, matching the
    /// negative case below), this demonstrates the sweep would catch that too (returns false →
    /// missingFromAllowlistedFiles in Part A).
    /// </summary>
    [Fact]
    public void ContainsRealAttributeUsage_DiscriminatesCodeFromCommentsAndDocs()
    {
        const string realCtorInjection = """
            public sealed class SomeQueryHandler(
                [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db)
            {
            }
            """;
        ContainsRealAttributeUsage(realCtorInjection).Should().BeTrue(
            "a real ctor-parameter attribute usage on a non-comment line must be detected");

        const string xmlDocMentionOnly = """
            /// <summary>
            /// Audit-capable handlers resolve the reader via
            /// <c>[FromKeyedServices(ReadConnectionKeys.Audit)]</c>.
            /// </summary>
            public sealed class SomeQueryHandler(IReadDbConnection db)
            {
            }
            """;
        ContainsRealAttributeUsage(xmlDocMentionOnly).Should().BeFalse(
            "a mention inside an XML-doc /// comment (as in ReadConnectionKeys.cs) must NOT count as a " +
            "real injection — otherwise every doc file referencing the attribute would false-positive " +
            "as an allowlist member");

        const string lineCommentMentionOnly = """
            // Read-path login isolation (ISO-DAPPER): audit-capable handlers resolve via
            // [FromKeyedServices(ReadConnectionKeys.Audit)]. Opt-in — falls back to Default.
            services.AddKeyedScoped<IReadDbConnection>(ReadConnectionKeys.Audit, (sp, _) => null!);
            """;
        ContainsRealAttributeUsage(lineCommentMentionOnly).Should().BeFalse(
            "a // line-comment mention (as in InfrastructureRegistration.cs) must NOT count as a real " +
            "injection — the actual code on the next line only calls AddKeyedScoped, it does not inject " +
            "the attribute onto a ctor parameter");

        const string noMentionAtAll = """
            public sealed class UnrelatedHandler(IReadDbConnection db)
            {
            }
            """;
        ContainsRealAttributeUsage(noMentionAtAll).Should().BeFalse(
            "a class with no mention of the attribute at all must not be flagged");
    }

    /// <summary>
    /// Returns true iff <paramref name="fileText"/> contains the literal
    /// <c>[FromKeyedServices(ReadConnectionKeys.Audit)]</c> attribute usage on a line that is NOT a
    /// comment. Comment lines are identified by their trimmed content starting with <c>//</c> — this
    /// also excludes <c>///</c> XML-doc lines, since every such line also starts with <c>//</c>.
    /// </summary>
    private static bool ContainsRealAttributeUsage(string fileText)
    {
        foreach (var rawLine in fileText.Split('\n'))
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.Contains(AuditKeyedAttribute, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Walks up from the test binary until it finds a directory containing both <c>src/</c> and
    /// <c>tests/</c>. Mirrors <see cref="AuditBoundaryEfWiringGuardTests"/>/<see cref="MarsConnectionStringGuardTests"/>.
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
