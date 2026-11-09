using System.Text.RegularExpressions;
using AqtCoreFW.Architecture.Tests.Fixtures;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guards against EF Core snapshot↔migration drift — the v2.133p12 → v2.133p13 incident class.
///
/// Two facts the suite enforces for every module's <c>Migrations/</c> folder:
/// <list type="number">
///   <item><c>AllModules_DesignerFiles_Should_HaveMatchingMigration</c> — every
///         <c>YYYYMMDDHHMMSS_*.Designer.cs</c> must have a sibling <c>.cs</c>.
///         A bare designer is strictly broken (EF can't apply a migration with no
///         <c>Up()</c>/<c>Down()</c>). The reverse is allowed: raw-SQL-only
///         migrations (e.g. <c>*AddRlsPolicies</c>) have a <c>.cs</c> without a
///         <c>.Designer.cs</c> by convention — no model change to snapshot.</item>
///   <item><c>AllModules_ModelSnapshot_Should_Match_LatestMigrationDesigner</c> — the
///         standalone <c>*ModelSnapshot.cs</c>'s <c>BuildModel</c> body must match
///         the latest migration's <c>BuildTargetModel</c> body, modulo whitespace.
///         EF regenerates them identically when <c>dotnet ef migrations add</c> runs;
///         a divergence means the snapshot was modified but the corresponding
///         migration was not generated/committed — the v2.133p12 drift class.</item>
/// </list>
///
/// Tolerates the Integration folder anomaly (<c>Migrations/</c> directly under
/// <c>*.Infrastructure</c> rather than <c>Persistence/Migrations/</c>) — see
/// <c>docs/code-standards.md</c> § "Migration Folder Convention".
/// </summary>
public sealed class MigrationConsistencyTests
{
    private static readonly Regex MigrationIdPattern = new(@"^\d{14}_", RegexOptions.Compiled);

    [Fact]
    public void AllModules_DesignerFiles_Should_HaveMatchingMigration()
    {
        var failures = new List<string>();

        foreach (var module in AssemblyFixtures.Modules)
        {
            var dir = ResolveMigrationsDir(module);
            if (dir is null) continue;

            var bare = new HashSet<string>(StringComparer.Ordinal);
            var designer = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in Directory.EnumerateFiles(dir, "*.cs"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!MigrationIdPattern.IsMatch(name)) continue;

                if (name.EndsWith(".Designer", StringComparison.Ordinal))
                {
                    designer.Add(name[..^".Designer".Length]);
                }
                else
                {
                    bare.Add(name);
                }
            }

            // Only flag designer-without-bare. The reverse (bare without designer)
            // is the legitimate raw-SQL-only convention used by *AddRlsPolicies.
            foreach (var id in designer.Except(bare))
                failures.Add($"{module.Name}: designer '{id}.Designer.cs' exists without matching '{id}.cs' in {dir}");
        }

        failures.Should().BeEmpty(
            because: "a Designer.cs without its sibling .cs migration cannot be applied by EF — " +
                     "indicates the migration was deleted or never committed. " +
                     "(The reverse — .cs without Designer.cs — is allowed for raw-SQL-only migrations.)");
    }

    [Fact]
    public void AllModules_ModelSnapshot_Should_Match_LatestMigrationDesigner()
    {
        var failures = new List<string>();

        foreach (var module in AssemblyFixtures.Modules)
        {
            var dir = ResolveMigrationsDir(module);
            if (dir is null) continue;

            // Exclude timestamp-prefixed migrations whose name happens to end in
            // "ModelSnapshot" (e.g. Audit's `20260316140615_SyncModelSnapshot.cs`).
            // The standalone snapshot is always `<DbContextName>ModelSnapshot.cs`.
            var snapshot = Directory.EnumerateFiles(dir, "*ModelSnapshot.cs")
                .Where(f => !MigrationIdPattern.IsMatch(Path.GetFileName(f)))
                .SingleOrDefault();
            if (snapshot is null) continue;

            var latestDesigner = Directory.EnumerateFiles(dir, "*.Designer.cs")
                .Where(f => MigrationIdPattern.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .FirstOrDefault();

            if (latestDesigner is null)
            {
                failures.Add($"{module.Name}: {Path.GetFileName(snapshot)} present but no '*.Designer.cs' migration found in {dir}");
                continue;
            }

            var snapshotBody = ExtractMethodBody(snapshot, "BuildModel");
            var migrationBody = ExtractMethodBody(latestDesigner, "BuildTargetModel");

            if (snapshotBody is null)
            {
                failures.Add($"{module.Name}: could not extract BuildModel body from {Path.GetFileName(snapshot)} (parser bug or unexpected layout)");
                continue;
            }
            if (migrationBody is null)
            {
                failures.Add($"{module.Name}: could not extract BuildTargetModel body from {Path.GetFileName(latestDesigner)} (parser bug or unexpected layout)");
                continue;
            }

            if (NormalizeWhitespace(snapshotBody) != NormalizeWhitespace(migrationBody))
            {
                failures.Add(
                    $"{module.Name}: ModelSnapshot.cs BuildModel body does not match latest migration " +
                    $"'{Path.GetFileNameWithoutExtension(latestDesigner)}' BuildTargetModel body. " +
                    "Indicates the snapshot was hand-edited OR a new column/entity was added but " +
                    "the corresponding migration was not generated/committed (v2.133p12 drift class).");
            }
        }

        failures.Should().BeEmpty(
            because: "EF Core regenerates ModelSnapshot.cs identically to the latest migration's " +
                     "Designer.cs whenever 'dotnet ef migrations add' runs — a divergence means " +
                     "the snapshot drifted from the migration history.");
    }

    /// <summary>
    /// Probe both supported Migrations folder layouts for the module:
    ///   - Standard: <c>src/modules/{name}/AqtCoreFW.{Name}.Infrastructure/Persistence/Migrations</c>
    ///   - Anomaly:  <c>src/modules/{name}/AqtCoreFW.{Name}.Infrastructure/Migrations</c>
    /// Returns null when the module has no migrations folder (allowed — no DbContext yet).
    /// </summary>
    private static string? ResolveMigrationsDir(ModuleAssemblies module)
    {
        var modulesRoot = LocateModulesRoot();
        var infraDir = Path.Combine(
            modulesRoot,
            module.Name.ToLowerInvariant(),
            $"AqtCoreFW.{module.Name}.Infrastructure");

        var standard = Path.Combine(infraDir, "Persistence", "Migrations");
        if (Directory.Exists(standard)) return standard;

        var anomaly = Path.Combine(infraDir, "Migrations");
        if (Directory.Exists(anomaly)) return anomaly;

        return null;
    }

    private static string LocateModulesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "modules");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate src/modules from {AppContext.BaseDirectory} walking up.");
    }

    /// <summary>
    /// Extract a method's body via brace-counting. Skips C-style comments and string
    /// literals so braces inside them don't perturb the depth counter. Returns null
    /// if the method header is not found or braces are unbalanced.
    /// </summary>
    private static string? ExtractMethodBody(string filePath, string methodName)
    {
        var text = File.ReadAllText(filePath);
        var headerPattern = new Regex(
            @"protected\s+override\s+void\s+" + Regex.Escape(methodName) +
            @"\s*\(\s*ModelBuilder\s+\w+\s*\)\s*",
            RegexOptions.Compiled);

        var match = headerPattern.Match(text);
        if (!match.Success) return null;

        var i = match.Index + match.Length;
        while (i < text.Length && text[i] != '{') i++;
        if (i >= text.Length) return null;

        var bodyStart = i + 1;
        var depth = 1;
        i = bodyStart;

        while (i < text.Length && depth > 0)
        {
            var c = text[i];

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i += 2;
                continue;
            }
            if (c == '"')
            {
                // EF-generated BuildModel/BuildTargetModel only emit regular "..." string
                // literals (column names, type names, schema names). No verbatim/raw strings
                // and no char literals are emitted by the EF scaffolder, verified across all
                // 17 modules. If EF ever changes this, the parser will need extending.
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i++;
                    i++;
                }
                i++;
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}') depth--;

            if (depth == 0) break;
            i++;
        }

        return depth == 0 ? text.Substring(bodyStart, i - bodyStart) : null;
    }

    /// <summary>
    /// Strip line + block comments, then collapse whitespace runs to a single space.
    /// Comments don't affect EF model semantics; allowing them lets hand-fix
    /// annotations live alongside the regenerated content (e.g. v2.133p14 Workflow
    /// Designer fix) without tripping the comparison.
    /// </summary>
    private static string NormalizeWhitespace(string s)
    {
        s = Regex.Replace(s, @"//[^\n]*", string.Empty);
        s = Regex.Replace(s, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
