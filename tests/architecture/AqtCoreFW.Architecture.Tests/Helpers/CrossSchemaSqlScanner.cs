using System.Text.RegularExpressions;

namespace AqtCoreFW.Architecture.Tests.Helpers;

/// <summary>
/// Source-text scanner that flags SQL string literals embedded in module code which
/// reference a SQL schema that does not belong to the owning module. Catches cross-schema
/// coupling on BOTH the read side (FROM/JOIN) and the write side (UPDATE/INSERT INTO/MERGE)
/// — a cross-schema write couples module ownership harder than a read, so both are flagged.
/// Phase 01 of the ModuleClient extraction prep removed the original JOIN-coupling sites.
///
/// Why source-text and not assembly metadata: SQL fragments live as compile-time
/// strings (raw triple-quoted, FormattableString, Dapper templates) — they are not
/// reachable through reflection. Roslyn would work but adds heavy dependency. A
/// regex+file-walk catches the >95% case at ms-level cost.
///
/// Limitations: does not detect EF Core <c>FromSqlRaw</c> with interpolated schema
/// names; does not parse fully — false positives possible inside line comments. Both
/// trade-offs are accepted; whitelist mechanism handles seed-only legitimate cases.
/// </summary>
public static class CrossSchemaSqlScanner
{
    /// <summary>
    /// Module directory name → SQL schema(s) the module legitimately owns. Driven by
    /// <c>HasDefaultSchema(...)</c> calls observed in each module's DbContext snapshot.
    /// Modules without a DbContext (e.g. <c>search</c>) appear with empty owned-schema set
    /// and consequently every cross-schema reference flags — those modules MUST go
    /// through ModuleClient interfaces, not raw SQL.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> ModuleToSchemas =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ai"] = ["ai"],
            ["audit"] = ["audit"],
            ["businessprocess"] = ["businessprocess"],
            ["collaboration"] = ["collab"],
            ["files"] = ["files"],
            ["forms"] = ["forms"],
            ["identity"] = ["identity"],
            ["integration"] = ["integration"],
            ["masterdata"] = ["masterdata"],
            ["notifications"] = ["notifications"],
            ["organization"] = ["organization"],
            ["reporting"] = ["reporting"],
            ["rules"] = ["rules"],
            ["search"] = Array.Empty<string>(),
            ["systemparams"] = ["config"],
            ["workflow"] = ["workflow"],
        };

    /// <summary>
    /// All known SQL schema names — used to distinguish cross-schema references from
    /// arbitrary identifiers that happen to use bracket+dot syntax.
    /// </summary>
    public static readonly IReadOnlySet<string> KnownSchemas = new HashSet<string>(
        ModuleToSchemas.Values.SelectMany(s => s),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whitelist of legitimate cross-schema references. Each entry MUST carry a Reason
    /// — drift control: PR review catches new entries without justification.
    /// </summary>
    public static readonly IReadOnlyCollection<WhitelistEntry> Whitelist =
    [
        new("SeedOrphanPurger.cs", "identity",
            "Reason: cross-schema cleanup of orphaned user rows by Identity-side purger; cross-schema by design."),
        // CSQ-2 (v2.132p10): wildcard tightened to explicit per-schema entries — drift control.
        // REMOVED 2026-05-01 (PR #71 cleanup): ReportDefinitionSeeder.cs cases + organization entries
        // deleted — Cases module removed, seeder class body emptied. No live SQL references remain.
        // REMOVED 2026-05-01 (PR #71 cleanup): OllamaReActAgent.cs cases entry deleted — grep
        // confirmed zero cases.* SQL in OllamaReActAgent.cs (Cases module removed PR #71).
        new("TenantUserSeeder.cs", "organization",
            "Reason: install-time tenant-agnostic user seeding loads each tenant's OrgUnit tree to place users; runs once during bootstrap, not request-path."),
        new("TenantAssignmentSeeder.cs", "identity",
            "Reason: install-time tenant-agnostic assignment seeding reads identity.AspNetUsers (by EmployeeCode prefix) to bridge org→user; runs once during bootstrap, not request-path."),
        new("OrgTreeSeeder.Treasury.cs", "masterdata",
            "Reason: install-time org-unit seeding pulls Wards reference data; runs once during bootstrap, not request-path."),
        new("OrgTreeSeeder.cs", "identity",
            "Reason: install-time org seeding checks the tenant row exists in identity.Tenants before seeding its tree (avoids orphaned org units); runs once during bootstrap, not request-path."),
        // CSQ-followup (v2.133p9): regression caught — TenantBackfillSeeder shipped v2.133p5
        // without whitelist entry. Architecture guard ACTIVE since v2.132p10.
        new("TenantBackfillSeeder.cs", "organization",
            "Reason: one-time IHostedService backfill — derives Tenants from DISTINCT TenantId across AspNetUsers ∪ OrgUnits. Runs at startup, not request-path; no IOrgUnitModuleClient surface fits one-shot backfill query (would create dead client API per YAGNI)."),
        // Phase-05 Wave-2 QĐ 742 D7 — SecurityHealth read-only aggregator. These handlers
        // live in Identity.Application but aggregate compliance data from [audit].* because
        // IAuditModuleClient has no per-criterion snapshot contract; adding one is YAGNI for
        // a read-only D7 dashboard used only by system admins. No write path; read replica only.
        new("GetAuditRetentionStatusQuery.cs", "audit",
            "Reason: D7 read-only compliance visibility — reads [audit].AuditRetentionPolicies + AuditLogEntries count for system-admin dashboard. No IAuditModuleClient snapshot contract exists (YAGNI). Read-only; no cross-module write coupling."),
        new("GetAuditIntegrityStatusQuery.cs", "audit",
            "Reason: D7 read-only compliance visibility — queries sys.triggers + [audit].AuditLogEntries total count for immutability guard status page. Read-only; no cross-module write coupling."),
    ];

    // Read + write keyword alternation shared by both patterns. FROM/JOIN cover reads (and
    // DELETE FROM, whose FROM branch matches); UPDATE / INSERT [INTO] / MERGE [INTO] cover writes
    // (INTO is optional in T-SQL for both INSERT and MERGE). Leading \b anchors each keyword to a
    // word boundary so a substring (e.g. "subMERGE into") cannot match. The KnownSchemas filter in
    // ScanLine further restricts hits to the 15 real schema names (so UPDATE STATISTICS dbo.X, a CTE
    // named "merge", INSERT INTO #temp, etc. cannot flag), making the broadening false-positive-safe.
    private const string SqlAccessKeywords = @"\b(?:FROM|JOIN|UPDATE|INSERT(?:\s+INTO)?|MERGE(?:\s+INTO)?)";

    /// <summary>Bracketed form: e.g. FROM [identity]. / UPDATE [audit]. / INSERT INTO [forms].</summary>
    private static readonly Regex BracketedSchemaPattern = new(
        SqlAccessKeywords + @"\s+\[(?<schema>\w+)\]\s*\.",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Un-bracketed form: e.g. FROM audit.X / UPDATE audit.X / MERGE INTO config.X.</summary>
    private static readonly Regex UnbracketedSchemaPattern = new(
        SqlAccessKeywords + @"\s+(?<schema>\w+)\.\w+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record WhitelistEntry(string FileName, string Schema, string Reason);

    public sealed record Violation(string FilePath, int LineNumber, string OwningModule, string ReferencedSchema, string Snippet);

    /// <summary>
    /// Scan a module source root recursively. Returns one violation per unique
    /// (file, line, schema) triple. Files outside <see cref="ModuleToSchemas"/>
    /// (e.g. shared infrastructure) are ignored — caller decides whether to scan them.
    /// </summary>
    public static IReadOnlyCollection<Violation> Scan(string srcModulesRoot)
    {
        if (!Directory.Exists(srcModulesRoot))
            throw new DirectoryNotFoundException($"src/modules not found at {srcModulesRoot}");

        var violations = new List<Violation>();
        foreach (var csFile in Directory.EnumerateFiles(srcModulesRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Skip generated EF migration / snapshot files — they legitimately reference any schema.
            if (csFile.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            if (csFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                csFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            var owningModule = DeriveOwningModule(csFile, srcModulesRoot);
            if (owningModule is null) continue;
            if (!ModuleToSchemas.TryGetValue(owningModule, out var ownedSchemas)) continue;

            var fileName = Path.GetFileName(csFile);
            var content = File.ReadAllText(csFile);
            var lines = content.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Skip single-line comments — references in /// XML doc + // notes are not
                // executable SQL. Multi-line /* ... */ comments are rare in this codebase
                // and not worth the parse cost for the false-negative trade.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                ScanLine(line, BracketedSchemaPattern, fileName, csFile, i + 1, owningModule, ownedSchemas, violations);
                ScanLine(line, UnbracketedSchemaPattern, fileName, csFile, i + 1, owningModule, ownedSchemas, violations);
            }
        }

        return violations;
    }

    private static void ScanLine(
        string line,
        Regex pattern,
        string fileName,
        string filePath,
        int lineNumber,
        string owningModule,
        IReadOnlyCollection<string> ownedSchemas,
        List<Violation> sink)
    {
        foreach (Match match in pattern.Matches(line))
        {
            var schema = match.Groups["schema"].Value;
            // Filter to known SQL schemas — un-bracketed regex matches arbitrary `Foo.Bar` identifiers.
            if (!KnownSchemas.Contains(schema)) continue;
            if (ownedSchemas.Contains(schema, StringComparer.OrdinalIgnoreCase)) continue;
            if (IsWhitelisted(fileName, schema)) continue;

            sink.Add(new Violation(filePath, lineNumber, owningModule, schema, line.Trim()));
        }
    }

    private static bool IsWhitelisted(string fileName, string schema)
    {
        foreach (var entry in Whitelist)
        {
            if (!string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Schema == "*" || string.Equals(entry.Schema, schema, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Path layout: <c>{root}/{module}/{Aqt...Project}/...</c>. The first segment after
    /// the modules root is the owning module directory name.
    /// </summary>
    private static string? DeriveOwningModule(string csFile, string srcModulesRoot)
    {
        var rel = Path.GetRelativePath(srcModulesRoot, csFile);
        var firstSep = rel.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return firstSep > 0 ? rel[..firstSep] : null;
    }
}
