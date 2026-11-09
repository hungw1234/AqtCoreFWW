using AqtCoreFW.Api;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Pure-function proof of the migration half of the deferred-module gate
/// (<see cref="StartupMigrationSets.EnabledContexts"/>), both branches:
/// default config keeps the full declared set (dev/test everything-on posture),
/// and each flag removes exactly its module's DbContext and nothing else.
/// The OFF-branch host boot itself is proven by
/// <c>DeferredModulesOffSmokeTests</c> (Startup.SmokeTests); this test pins the
/// filter's arithmetic without a container.
/// </summary>
public sealed class DeferredModuleFlagMigrationFilterTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    [Fact]
    public void DefaultConfig_MissingKeys_KeepsFullDeclaredSet()
    {
        var enabled = StartupMigrationSets.EnabledContexts(Config());

        // Missing key = enabled: today's dev/test behavior must be byte-identical.
        enabled.Should().Equal(StartupMigrationSets.Contexts);
    }

    [Fact]
    public void ExplicitTrue_KeepsFullDeclaredSet()
    {
        var pairs = DeferredModuleFlags.ModuleNames
            .Select(n => ($"Features:{n}:Enabled", "true"))
            .ToArray();

        StartupMigrationSets.EnabledContexts(Config(pairs))
            .Should().Equal(StartupMigrationSets.Contexts);
    }

    [Theory]
    [InlineData(DeferredModuleFlags.Ai)]
    [InlineData(DeferredModuleFlags.Collaboration)]
    [InlineData(DeferredModuleFlags.BusinessProcess)]
    [InlineData(DeferredModuleFlags.Reporting)]
    [InlineData(DeferredModuleFlags.Rules)]
    public void SingleFlagOff_RemovesExactlyThatModulesContext(string moduleName)
    {
        var config = Config(($"Features:{moduleName}:Enabled", "false"));
        var removedContext = DeferredModuleFlags.ModuleDbContexts[moduleName];

        var enabled = StartupMigrationSets.EnabledContexts(config);

        enabled.Should().NotContain(removedContext);
        enabled.Should().Equal(StartupMigrationSets.Contexts.Where(t => t != removedContext),
            "only the disabled module's context may be removed, and declared order must be preserved");
    }

    [Fact]
    public void AllFlagsOff_RemovesAllFiveAndOnlyThose()
    {
        var pairs = DeferredModuleFlags.ModuleNames
            .Select(n => ($"Features:{n}:Enabled", "false"))
            .ToArray();

        var enabled = StartupMigrationSets.EnabledContexts(Config(pairs));

        enabled.Should().Equal(
            StartupMigrationSets.Contexts.Where(
                t => !DeferredModuleFlags.ModuleDbContexts.Values.Contains(t)));
        enabled.Should().HaveCount(StartupMigrationSets.Contexts.Count - DeferredModuleFlags.ModuleNames.Count);
    }

    /// <summary>
    /// Blind-spot guard (source text-parse, precedent: SeederStartupOrderGuardTests): Reporting's
    /// recurring job is registered by a STATIC Hangfire facade call in Program.cs — outside
    /// AddReportingInfrastructure, so the DI gate does not cover it, and the OFF-branch smoke
    /// fixture cannot catch a leak because it boots with Hangfire:RegisterRecurringJobs=false.
    /// Ungated, an OFF deployment upserts a job whose type cannot be activated → a nightly
    /// activation-failure loop. This pins BOTH branches: registration gated on the flag, and
    /// RemoveIfExists for a schedule persisted by a previously-ON deployment.
    /// </summary>
    [Fact]
    public void ReportingRecurringJobRegistration_IsGatedOnTheFlag_WithRemoveOnOff()
    {
        var programPath = Path.Combine(
            RepoRoot(), "src", "host", "AqtCoreFW.Api", "Program.cs");
        var source = File.ReadAllText(programPath);

        var gateIdx = source.IndexOf("if (reportingEnabled)", StringComparison.Ordinal);
        var registerIdx = source.IndexOf("AddReportingRecurringJobs()", StringComparison.Ordinal);
        var removeIdx = source.IndexOf(
            "RecurringJob.RemoveIfExists(\"reporting.report-cleanup\")", StringComparison.Ordinal);

        registerIdx.Should().BeGreaterThan(0, "the Reporting recurring-job registration must exist");
        gateIdx.Should().BeInRange(1, registerIdx,
            "AddReportingRecurringJobs must sit behind an if (reportingEnabled) gate");
        removeIdx.Should().BeGreaterThan(registerIdx,
            "the off-branch must RemoveIfExists the persisted schedule from a previously-ON deployment");
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "AqtCoreFW.sln"))
               && !File.Exists(Path.Combine(dir, "src", "AqtCoreFW.slnx")))
        {
            dir = Path.GetDirectoryName(dir)
                ?? throw new InvalidOperationException("Repo root not found from test base directory.");
        }
        return dir;
    }

    /// <summary>
    /// Wiring completeness: every declared deferrable module's DbContext must actually be a
    /// member of the declared migration set — a typo'd or stale type in
    /// <see cref="DeferredModuleFlags.ModuleDbContexts"/> would silently filter nothing.
    /// </summary>
    [Fact]
    public void EveryDeferrableModulesContext_IsInTheDeclaredSet()
    {
        foreach (var (moduleName, contextType) in DeferredModuleFlags.ModuleDbContexts)
        {
            StartupMigrationSets.Contexts.Should().Contain(contextType,
                $"DeferredModuleFlags maps {moduleName} to {contextType.Name}, which must exist in the declared migration set");
        }
    }
}
