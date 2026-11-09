using AqtCoreFW.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="StartupMigrationMode"/> — the <c>--migrate-only</c> / <c>RUN_MIGRATIONS_ONLY</c> /
/// <c>SKIP_STARTUP_MIGRATIONS</c> startup gate (backlog <c>RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS</c>).
/// </summary>
public sealed class StartupMigrationModeTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    // ── Arg detection / stripping ──────────────────────────────────────────

    [Fact]
    public void HasMigrateOnlyArg_PresentToken_ReturnsTrue() =>
        StartupMigrationMode.HasMigrateOnlyArg(["--urls", "http://*:5000", "--migrate-only"]).Should().BeTrue();

    [Fact]
    public void HasMigrateOnlyArg_Absent_ReturnsFalse() =>
        StartupMigrationMode.HasMigrateOnlyArg(["--migrate", "--migrate-only=true"]).Should().BeFalse();

    [Fact]
    public void HasMigrateOnlyArg_NullOrEmpty_ReturnsFalse()
    {
        StartupMigrationMode.HasMigrateOnlyArg([]).Should().BeFalse();
        StartupMigrationMode.HasMigrateOnlyArg(null!).Should().BeFalse();
    }

    [Fact]
    public void StripKnownArgs_RemovesOnlyMigrateOnly_PreservesEverythingElse()
    {
        string[] input = ["--migrate-only", "--urls=http://*:5000", "--Foo:Bar", "value"];
        StartupMigrationMode.StripKnownArgs(input).Should().Equal("--urls=http://*:5000", "--Foo:Bar", "value");
    }

    [Fact]
    public void StripKnownArgs_NoMigrateOnly_ReturnsEquivalentSequence() =>
        StartupMigrationMode.StripKnownArgs(["--urls=http://*:5000"]).Should().Equal("--urls=http://*:5000");

    [Fact]
    public void StripKnownArgs_Null_ReturnsEmpty() =>
        StartupMigrationMode.StripKnownArgs(null!).Should().BeEmpty();

    // ── ParseBool ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData(" true ")]
    [InlineData("1")]
    public void ParseBool_TruthyValues_ReturnTrue(string value) =>
        StartupMigrationMode.ParseBool(value).Should().BeTrue();

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("yes")]
    [InlineData("on")]
    public void ParseBool_FalsyOrUnrecognised_ReturnFalse(string? value) =>
        StartupMigrationMode.ParseBool(value).Should().BeFalse();

    // ── Resolve ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoArgNoConfig_IsDefaultRunMigrationsAndServe()
    {
        var mode = StartupMigrationMode.Resolve(hasMigrateOnlyArg: false, Config());

        mode.MigrateOnly.Should().BeFalse();
        mode.SkipStartupMigrations.Should().BeFalse();
        mode.RunMigrations.Should().BeTrue();
        mode.ExitAfterMigrations.Should().BeFalse();
    }

    [Fact]
    public void Resolve_MigrateOnlyArg_RunsAndExits()
    {
        var mode = StartupMigrationMode.Resolve(hasMigrateOnlyArg: true, Config());

        mode.MigrateOnly.Should().BeTrue();
        mode.RunMigrations.Should().BeTrue();
        mode.ExitAfterMigrations.Should().BeTrue();
    }

    [Fact]
    public void Resolve_RunMigrationsOnlyConfig_EquivalentToArg()
    {
        var mode = StartupMigrationMode.Resolve(
            hasMigrateOnlyArg: false,
            Config((StartupMigrationMode.RunMigrationsOnlyKey, "true")));

        mode.MigrateOnly.Should().BeTrue();
        mode.ExitAfterMigrations.Should().BeTrue();
    }

    [Fact]
    public void Resolve_SkipStartupMigrations_SkipsButStillServes()
    {
        var mode = StartupMigrationMode.Resolve(
            hasMigrateOnlyArg: false,
            Config((StartupMigrationMode.SkipStartupMigrationsKey, "true")));

        mode.SkipStartupMigrations.Should().BeTrue();
        mode.RunMigrations.Should().BeFalse();
        mode.ExitAfterMigrations.Should().BeFalse();
    }

    [Fact]
    public void Resolve_MigrateOnly_TakesPrecedenceOver_SkipStartupMigrations()
    {
        // Migrate Job inheriting SKIP_STARTUP_MIGRATIONS from a shared ConfigMap must still migrate.
        var mode = StartupMigrationMode.Resolve(
            hasMigrateOnlyArg: true,
            Config((StartupMigrationMode.SkipStartupMigrationsKey, "true")));

        mode.MigrateOnly.Should().BeTrue();
        mode.SkipStartupMigrations.Should().BeTrue();
        mode.RunMigrations.Should().BeTrue("--migrate-only overrides SKIP_STARTUP_MIGRATIONS");
        mode.ExitAfterMigrations.Should().BeTrue();
    }

    [Fact]
    public void Resolve_NullConfig_Throws() =>
        ((Action)(() => StartupMigrationMode.Resolve(false, null!))).Should().Throw<ArgumentNullException>();
}
