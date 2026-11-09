using System.Text.RegularExpressions;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guards against stale or missing outbox schema registrations — the 2026-05-02 incident class.
///
/// Symptom of drift: <c>OutboxDispatcher</c> polls a registered schema whose backing DB schema
/// no longer exists (module removed but registration line forgotten) → <c>SqlException 208
/// "Invalid object name 'X.OutboxMessages'"</c> every polling cycle. Inverse drift (DbContext
/// added but registration forgotten) silently breaks event publication for that module.
///
/// Two-way invariant enforced here, by parsing source text directly (no DI/EF required):
/// <list type="number">
///   <item>Every <c>services.AddOutboxSchema("X")</c> in
///         <c>src/shared/AqtCoreFW.Infrastructure/InfrastructureRegistration.cs</c> must have a
///         matching <c>ModuleDbContext</c> subclass somewhere under <c>src/modules</c> declaring
///         <c>protected override string SchemaName =&gt; "X"</c>.</item>
///   <item>Every <c>ModuleDbContext</c> subclass declaring <c>SchemaName =&gt; "X"</c> must have
///         a matching <c>AddOutboxSchema("X")</c> registration.</item>
/// </list>
/// </summary>
public sealed class OutboxSchemaRegistrationTests
{
    private static readonly Regex AddOutboxSchemaPattern = new(
        @"AddOutboxSchema\s*\(\s*""([^""]+)""\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex SchemaNamePattern = new(
        @"protected\s+override\s+string\s+SchemaName\s*=>\s*""([^""]+)""\s*;",
        RegexOptions.Compiled);

    [Fact]
    public void EveryRegisteredOutboxSchema_Should_HaveCorrespondingModuleDbContext()
    {
        var registered = ParseRegisteredSchemas();
        var declared = ParseDeclaredSchemas();

        var orphanedRegistrations = registered.Except(declared).ToList();

        orphanedRegistrations.Should().BeEmpty(
            because: "every AddOutboxSchema(name) call must have a ModuleDbContext subclass declaring " +
                     "the same SchemaName — otherwise OutboxDispatcher polls a non-existent table " +
                     "(SqlException 208). Likely cause: module was removed but registration line in " +
                     "src/shared/AqtCoreFW.Infrastructure/InfrastructureRegistration.cs was forgotten. " +
                     "Orphaned schemas: " + string.Join(", ", orphanedRegistrations));
    }

    [Fact]
    public void EveryModuleDbContextSchema_Should_HaveOutboxSchemaRegistration()
    {
        var registered = ParseRegisteredSchemas();
        var declared = ParseDeclaredSchemas();

        var unregisteredSchemas = declared.Except(registered).ToList();

        unregisteredSchemas.Should().BeEmpty(
            because: "every ModuleDbContext SchemaName must be registered with AddOutboxSchema(...) " +
                     "in src/shared/AqtCoreFW.Infrastructure/InfrastructureRegistration.cs — otherwise " +
                     "outbox rows captured by the OutboxInterceptor never get dispatched and events " +
                     "for that module silently disappear. Unregistered schemas: " +
                     string.Join(", ", unregisteredSchemas));
    }

    private static HashSet<string> ParseRegisteredSchemas()
    {
        var path = Path.Combine(
            LocateRepoRoot(),
            "src", "shared", "AqtCoreFW.Infrastructure", "InfrastructureRegistration.cs");

        File.Exists(path).Should().BeTrue(because: $"expected shared registration file at {path}");

        var text = File.ReadAllText(path);
        return AddOutboxSchemaPattern.Matches(text)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ParseDeclaredSchemas()
    {
        var modulesRoot = Path.Combine(LocateRepoRoot(), "src", "modules");
        Directory.Exists(modulesRoot).Should().BeTrue(because: $"expected modules dir at {modulesRoot}");

        var schemas = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(modulesRoot, "*DbContext.cs", SearchOption.AllDirectories))
        {
            // Skip generated migrations / snapshots / Designer.cs — those reference DbContext but
            // don't declare new ones. Filename matches *DbContext.cs but not *DbContextModelSnapshot.cs.
            var name = Path.GetFileName(file);
            if (name.Contains("Snapshot", StringComparison.Ordinal)) continue;
            if (name.EndsWith(".Designer.cs", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);

            // Only consider files that subclass ModuleDbContext (excludes Identity/Audit which use
            // their own base classes — those don't go through the outbox dispatcher).
            if (!text.Contains(": ModuleDbContext", StringComparison.Ordinal)) continue;

            var match = SchemaNamePattern.Match(text);
            if (match.Success) schemas.Add(match.Groups[1].Value);
        }

        return schemas;
    }

    /// <summary>
    /// Walk up from the test assembly location until we find <c>src/</c>. Robust to bin/Debug/net10.0
    /// nesting depth across machines + CI. Mirrors helpers in <see cref="ModuleIsolationTests"/>.
    /// </summary>
    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate repo root (parent of src/) from {AppContext.BaseDirectory} walking up.");
    }
}
