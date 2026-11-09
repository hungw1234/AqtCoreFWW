using System.Reflection;
using System.Text.RegularExpressions;
using AqtCoreFW.Architecture.Tests.Fixtures;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guards against module-shipping wiring gaps — the 2026-05-17 v2.137p51 + p52 incident class.
///
/// When a new module ships (e.g. BusinessProcess MVP P3 on 2026-05-15), it requires THREE
/// separate wirings beyond DI registration:
/// <list type="number">
///   <item><b>Startup migration:</b> the module's DbContext must be listed in
///         <c>AqtCoreFW.Api.StartupMigrationSets.Contexts</c> (the data-driven set the host
///         iterates and migrates at boot). Gap → schema never created → SqlException 208 on
///         every outbox dispatcher cycle + BP commands silently fail to persist outbox rows.
///         (Fixed v2.137p51.) This check reads the declared set by reflection rather than
///         text-scanning Program.cs, so it is robust to how the host invokes MigrateAsync.</item>
///   <item><b>AppMenu seeder entry:</b> <c>E2ETestDataSeeder.Menus.cs</c> must seed an
///         <c>M(...)</c> row + <c>P(...)</c> perm wire for the module's admin page. Gap →
///         DB-driven menu in <c>[identity].AppMenus</c> has no entry, sidebar bypasses
///         hardcoded fallback (49 rows non-empty), menu invisible despite SystemAdmin
///         holding the VIEW perm. (Fixed v2.137p52.)</item>
///   <item>DI registration via <c>AddDbContext</c>. (Already correct in both incidents —
///         this is the only wiring that fails loudly, hence the gap on the other two.)</item>
/// </list>
///
/// Both tests parse source text directly (no DI/EF required) — same shape as
/// <see cref="OutboxSchemaRegistrationTests"/>.
/// </summary>
public sealed class ModuleStartupWiringTests
{
    // ── Test 1: DbContext ↔ MigrateAsync wiring ──────────────────────────────────

    private static readonly Regex AddDbContextPattern = new(
        @"services\.AddDbContext<(\w+DbContext)>",
        RegexOptions.Compiled);

    [Fact]
    public void EveryRegisteredDbContext_Should_BeInApiStartupMigrateSet()
    {
        var registered = ParseRegisteredDbContexts();
        var migrated = ParseMigratedDbContexts();

        var missingMigrations = registered.Except(migrated).ToList();

        missingMigrations.Should().BeEmpty(
            because: "every services.AddDbContext<XxxDbContext>(...) in a module Infrastructure " +
                     "registration must also appear in AqtCoreFW.Api.StartupMigrationSets.Contexts — the " +
                     "data-driven set the host migrates at boot — otherwise the module's schema is never " +
                     "created and runtime fails with SqlException 208 + silent outbox loss (v2.137p51 " +
                     "incident: BP MVP P3 shipped DbContext + DI registration but forgot the migrate wiring). " +
                     "Missing from the migrate set: " + string.Join(", ", missingMigrations));
    }

    /// <summary>
    /// Anti-false-green cross-check for <see cref="ParseRegisteredDbContexts"/>: the text parser must
    /// surface EVERY real DbContext, regardless of the registration file's name. Ground truth comes from
    /// reflection over each module's Infrastructure assembly (cannot drift from the actual types), so a
    /// parser that only matches a file-name convention (e.g. <c>*InfrastructureRegistration.cs</c>) is
    /// caught here — five modules name their registration file differently
    /// (Forms/Workflow/MasterData/Organization/SystemParams: <c>*Module.cs</c> / <c>*Registration.cs</c>)
    /// and were silently skipped, making <see cref="EveryRegisteredDbContext_Should_BeInApiStartupMigrateSet"/>
    /// blind to a missing MigrateAsync for any of them.
    /// </summary>
    [Fact]
    public void TextParser_Should_Discover_EveryModuleDbContext()
    {
        var actual = AssemblyFixtures.Modules
            .SelectMany(m => SafeGetTypes(m.Infrastructure))
            .Where(t => !t.IsAbstract && DerivesFromDbContext(t))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        actual.Should().NotBeEmpty(because: "modules expose at least one EF Core DbContext each");

        var undiscovered = actual.Except(ParseRegisteredDbContexts()).OrderBy(n => n, StringComparer.Ordinal).ToList();

        undiscovered.Should().BeEmpty(
            because: "ParseRegisteredDbContexts() must scan every module's *.Infrastructure project " +
                     "(directory-based), not glob a single file-name convention — otherwise modules whose " +
                     "registration file is not named *InfrastructureRegistration.cs are invisible to the " +
                     "MigrateAsync wiring guard. Undiscovered DbContexts: " + string.Join(", ", undiscovered));
    }

    private static bool DerivesFromDbContext(Type type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (b.FullName == "Microsoft.EntityFrameworkCore.DbContext") return true;
        return false;
    }

    /// <summary>
    /// Reflects an assembly's types, tolerating a partially-loadable assembly: a single type whose
    /// dependency fails to resolve would otherwise throw and mask the assertion message. Returns the
    /// loadable subset (sufficient for DbContext discovery — DbContexts have no exotic load deps).
    /// </summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static HashSet<string> ParseRegisteredDbContexts()
    {
        var modulesRoot = Path.Combine(LocateRepoRoot(), "src", "modules");
        Directory.Exists(modulesRoot).Should().BeTrue(because: $"expected modules dir at {modulesRoot}");

        var dbContexts = new HashSet<string>(StringComparer.Ordinal);

        // Scan every *.cs inside each module's *.Infrastructure PROJECT directory (directory-based,
        // file-name-agnostic). Registration files do not follow a single naming convention
        // (*InfrastructureRegistration.cs / *Registration.cs / *Module.cs), so a name glob silently
        // skipped 5 modules — guarded by TextParser_Should_Discover_EveryModuleDbContext.
        foreach (var infraDir in Directory.EnumerateDirectories(
            modulesRoot, "*.Infrastructure", SearchOption.AllDirectories))
        {
            foreach (var file in Directory.EnumerateFiles(infraDir, "*.cs", SearchOption.AllDirectories))
            {
                // Skip bin/obj copies.
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

                var text = File.ReadAllText(file);
                foreach (Match m in AddDbContextPattern.Matches(text))
                    dbContexts.Add(m.Groups[1].Value);
            }
        }

        return dbContexts;
    }

    /// <summary>
    /// The set of DbContexts the Api host migrates at startup, read from the declared source of
    /// truth (<c>AqtCoreFW.Api.StartupMigrationSets.Contexts</c>) by reflection. Replaces the prior
    /// brittle regex text-scan of Program.cs — which broke the moment the host switched from inline
    /// <c>MigrateAsync</c> calls to iterating the declared set (the exact brittleness ADR-010's
    /// migrate-subset follow-up called out).
    /// </summary>
    private static HashSet<string> ParseMigratedDbContexts() =>
        AqtCoreFW.Api.StartupMigrationSets.Contexts
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

    // ── Test 2: Module VIEW permission ↔ AppMenu seeder coverage ─────────────────

    private static readonly Regex SeededPermViewPattern = new(
        @"\[""(\w+)\.(\w+)\.VIEW""\]\s*=",
        RegexOptions.Compiled);

    private static readonly Regex SeededMenuPermPattern = new(
        @"P\s*\(\s*Menu\w+Id\s*,\s*""(\w+)\.(\w+)\.(\w+)""\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Modules with seeded VIEW permissions but no admin menu — intentionally headless or
    /// surfaced via in-page actions instead of a sidebar entry. Adding a module here is an
    /// explicit opt-out; the default expectation is "if you ship a *.VIEW perm, ship a menu".
    /// Keep this list small + comment each entry.
    /// </summary>
    private static readonly HashSet<string> NoMenuModulePrefixesAllowlist = new(StringComparer.Ordinal)
    {
        // PLATFORM.* is SystemAdmin-only via direct route navigation, no sidebar entry by design.
        // (Phase-00 D6 — PLATFORM tier is tenant-Admin excluded; SystemAdmin uses /admin/...
        // routes that aren't in the AppMenus seeder.)
        "PLATFORM",
        // SYSTEM.CONFIG.MANAGE is access via /admin/system-params (covered by SYSTEMPARAM module).
        // No standalone SYSTEM.* VIEW perm exists — but if one is added later without a menu it
        // would be tenant-Admin-locked admin shell access.
        "SYSTEM",
        // COMPLIANCE.CLASSIFICATION.* is SoD-locked to SystemAdmin via direct API; no menu by design
        // (Phase-04 §256). VIEW perms exist but route is admin-shell only.
        "COMPLIANCE",
    };

    [Fact]
    public void EveryModuleWith_ViewPermissions_Should_HaveAppMenuEntry()
    {
        var permPrefixes = ParseSeededViewPermModulePrefixes();
        var menuPrefixes = ParseSeededMenuPermModulePrefixes();

        var missing = permPrefixes
            .Except(menuPrefixes)
            .Except(NoMenuModulePrefixesAllowlist)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            because: "every module that seeds a *.VIEW permission in E2ETestDataSeeder.Permissions.cs " +
                     "must also seed at least one AppMenu entry wired to that module's permission via " +
                     "P(MenuXxxId, \"MODULE.RESOURCE.PERM\") in E2ETestDataSeeder.Menus.cs — otherwise " +
                     "the DB-driven menu in [identity].AppMenus has no entry, sidebar bypasses the " +
                     "hardcoded fallback (admin-menu-entries.ts), and the page is invisible despite the " +
                     "perm being granted (v2.137p52 incident: BP MVP P3 shipped 3 BP perms but no menu " +
                     "seeder entry). If a module is intentionally headless, add its prefix to " +
                     "NoMenuModulePrefixesAllowlist with a justifying comment. Module prefixes with " +
                     "VIEW perms but no menu: " + string.Join(", ", missing));
    }

    private static HashSet<string> ParseSeededViewPermModulePrefixes()
    {
        var path = Path.Combine(
            LocateRepoRoot(), "src", "host", "AqtCoreFW.Api", "E2ETestDataSeeder.Permissions.cs");
        File.Exists(path).Should().BeTrue(because: $"expected perm seeder at {path}");

        var text = File.ReadAllText(path);
        return SeededPermViewPattern.Matches(text)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ParseSeededMenuPermModulePrefixes()
    {
        var path = Path.Combine(
            LocateRepoRoot(), "src", "host", "AqtCoreFW.Api", "E2ETestDataSeeder.Menus.cs");
        File.Exists(path).Should().BeTrue(because: $"expected menu seeder at {path}");

        var text = File.ReadAllText(path);
        return SeededMenuPermPattern.Matches(text)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    // ── Shared helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Walk up from the test assembly location until we find <c>src/</c>. Robust to bin/Debug/net10.0
    /// nesting depth across machines + CI. Mirrors helpers in <see cref="OutboxSchemaRegistrationTests"/>.
    /// </summary>
    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate repo root (no parent of {AppContext.BaseDirectory} contains 'src/').");
    }
}
