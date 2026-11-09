using System.IO;
using System.Linq;
using AqtCoreFW.Tests.SharedFixtures;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Guards that a smoke-fixture host boot is immune to a foreign <c>ConnectionStrings__Default</c>
/// environment variable — the invariant that closes the fixture connection-string race.
///
/// Background: the three smoke host fixtures used to hoist their connection string into the
/// process-global <c>ConnectionStrings__Default</c> env var, because the API/AuthServer
/// <c>Program.cs</c> reads it at the ConfigurationManager phase (DbContext / Hangfire-storage
/// registration) which does not see a WebApplicationFactory's later AddInMemoryCollection.
/// One shared env-var slot + overlapping fixture lifecycles = hosts booting against the wrong
/// (or a disposed) container → "connection refused", 6/9 smoke classes red in a full run.
///
/// The fix injects the connection string via <c>builder.UseSetting</c> (UseTestConnectionStrings),
/// which WebApplicationFactory turns into a highest-precedence command-line configuration source.
/// A stray env var can therefore no longer win. These tests encode that guarantee directly, so a
/// regression that reintroduces env-var hoisting fails loudly and deterministically.
/// </summary>
[Collection(SmokeTestCollection.CollectionName)]
public sealed class FixtureConnectionStringIsolationTests
    : IClassFixture<ApiHostFixture>
{
    private readonly ApiHostFixture _fixture;

    public FixtureConnectionStringIsolationTests(ApiHostFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Sets a garbage <c>ConnectionStrings__Default</c> env var AFTER the fixture has its own
    /// container, then boots the host. Pre-fix: the env var wins at the ConfigurationManager phase
    /// → the host points at a non-existent server → boot throws. Post-fix: the fixture's UseSetting
    /// value (command-line source) outranks the env var → the host boots against its own container
    /// and <c>/health/live</c> returns 200. Restores the env var in <c>finally</c>.
    /// </summary>
    [Fact, Trait("Category", "Smoke")]
    public async Task ApiHost_Boot_IgnoresForeignConnectionStringEnvVar()
    {
        const string key = "ConnectionStrings__Default";
        var original = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(
            key,
            "Server=127.0.0.1,59999;Database=nonexistent_isolation_probe;User Id=sa;Password=wrong;TrustServerCertificate=true;Connect Timeout=3");

        try
        {
            // Forces the host to build against the poisoned env var. With the fix, UseSetting's
            // command-line source outranks it and the boot succeeds against the real container.
            var client = _fixture.CreateClient();
            var response = await client.GetAsync("/health/live");

            Assert.True(
                response.IsSuccessStatusCode,
                $"Expected /health/live 200 but got {(int)response.StatusCode}. A non-200 (or a boot " +
                "exception) means a foreign ConnectionStrings__Default env var overrode the fixture's " +
                "UseSetting connection string — the fixture-isolation invariant is broken.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

}

/// <summary>
/// Static-source guard (no host boot, no fixture): no smoke fixture may set
/// <c>ConnectionStrings__*</c> as a process-global env var again. Re-growing that pattern
/// reintroduces the shared-slot race this project fixed — fixtures must route connection strings
/// through <c>UseTestConnectionStrings</c> instead. Kept in its own fixture-free class so it never
/// depends on host-boot ordering. Mirrors the repo's static-source arch-test precedent
/// (MarsConnectionStringGuardTests).
/// </summary>
[Trait("Category", "Smoke")]
public sealed class SmokeFixtureEnvVarStaticGuardTests
{
    [Fact, Trait("Category", "Smoke")]
    public void NoSmokeFixture_SetsConnectionStringEnvVar()
    {
        var projectDir = FindSmokeProjectDir();
        var offenders = Directory
            .EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/")
                     && !f.Replace('\\', '/').Contains("/bin/"))
            .Where(f => File.ReadAllText(f)
                .Contains("SetEnvironmentVariable(\"ConnectionStrings"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Smoke fixtures must inject connection strings via UseTestConnectionStrings, not a " +
            "process-global env var (shared-slot race). Offending file(s): " +
            string.Join(", ", offenders));
    }

    private static string FindSmokeProjectDir()
    {
        // Walk up from the test binary until the project's .csproj is found.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !File.Exists(Path.Combine(dir.FullName, "AqtCoreFW.Startup.SmokeTests.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
