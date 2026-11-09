using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guard G2 (ADR-011 rule 2 — "Presentation depends only on Application + SharedKernel").
///
/// No <c>*.Presentation.csproj</c> may carry a <c>&lt;ProjectReference&gt;</c> to a module
/// Infrastructure project (<c>AqtCoreFW.&lt;Module&gt;.Infrastructure</c>). Cross-module access goes
/// through Application abstractions.
///
/// Why this scans the .csproj text and NOT runtime <c>Assembly.GetReferencedAssemblies()</c>: the C#
/// compiler elides a reference whose types are never used. <c>SystemParams.Presentation</c> declares
/// a ProjectReference to its Infrastructure project but uses no type from it — a runtime check would
/// not see the dead reference and the guard would miss the very coupling rule 2 forbids. The
/// architectural rule is about the declared dependency edge, so the project file is the source of truth.
/// </summary>
public sealed class PresentationDependsOnlyOnApplicationGuardTests
{
    // <ProjectReference Include="..\AqtCoreFW.<Module>.Infrastructure\AqtCoreFW.<Module>.Infrastructure.csproj" />
    private static readonly Regex InfrastructureProjectReference =
        new(@"ProjectReference\s+Include\s*=\s*""[^""]*AqtCoreFW\.[A-Za-z]+\.Infrastructure\.csproj""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Presentation projects that still reference a module Infrastructure project. Key = project file
    /// name, Value = reason + removing phase. Shrinks toward the single documented MasterData carve-out
    /// (see ARCH-MASTERDATA-PRESENTATION-APP-ABSTRACTION); the end-state is asserted in Phase-6 closure.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownInfraReferencingPresentation =
        new Dictionary<string, string>(); // empty — no *.Presentation project references a module Infrastructure project

    [Fact]
    public void NoPresentationProject_ReferencesModuleInfrastructure()
    {
        var violations = FindPresentationProjects()
            .Where(p => InfrastructureProjectReference.IsMatch(File.ReadAllText(p)))
            .Select(Path.GetFileName)
            .Where(name => !KnownInfraReferencingPresentation.ContainsKey(name!))
            .OrderBy(name => name)
            .ToList();

        violations.Should().BeEmpty(
            "a *.Presentation project must reference only *.Application + SharedKernel — route " +
            "Infrastructure usage through an Application abstraction. New violators: {0}",
            string.Join(", ", violations));
    }

    [Fact]
    public void Closure_AllowlistIsEmpty()
    {
        // Every *.Presentation project now references only *.Application + SharedKernel. Pin the empty
        // end-state so re-adding a module-Infrastructure project reference fails here.
        KnownInfraReferencingPresentation.Should().BeEmpty(
            "no *.Presentation project may reference a module Infrastructure project");
    }

    [Fact]
    public void Discovery_FindsEveryModulePresentationProject()
    {
        // Anti-vacuous: if the glob returns an empty/partial set, the violation check passes for the
        // wrong reason. There are 15 module Presentation projects.
        var projects = FindPresentationProjects().ToList();

        projects.Count.Should().BeGreaterThanOrEqualTo(15,
            "expected at least 15 module *.Presentation projects under src/modules; a smaller count means " +
            "the project glob silently under-discovered and the guard is vacuous. Found: {0}",
            string.Join(", ", projects.Select(Path.GetFileName)));
    }

    [Fact]
    public void Allowlist_EntriesStillReferenceInfrastructure()
    {
        // Tripwire: once a phase drops the dead/real reference, its lingering allowlist entry must FAIL
        // here so the allowlist is cleaned up in the same commit (no vacuous existence pass).
        var byName = FindPresentationProjects().ToDictionary(p => Path.GetFileName(p)!, p => p);

        var stale = KnownInfraReferencingPresentation.Keys
            .Where(name => !byName.TryGetValue(name, out var path) ||
                           !InfrastructureProjectReference.IsMatch(File.ReadAllText(path!)))
            .ToList();

        stale.Should().BeEmpty(
            "an allowlist entry must name a Presentation project that STILL references a module " +
            "Infrastructure project. Stale entries (reference already removed): {0}",
            string.Join(", ", stale));
    }

    // ── Project discovery ───────────────────────────────────────────────────────

    private static IEnumerable<string> FindPresentationProjects()
    {
        var modulesRoot = Path.Combine(RepoRoot(), "src", "modules");
        return Directory.EnumerateFiles(modulesRoot, "*.Presentation.csproj", SearchOption.AllDirectories);
    }

    /// <summary>Ascends from the test bin directory until it finds the repo's src/AqtCoreFW.slnx.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "AqtCoreFW.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repo root (no src/AqtCoreFW.slnx found ascending from " +
            $"{AppContext.BaseDirectory}). G2 scans .csproj files and needs the repo root.");
    }
}
