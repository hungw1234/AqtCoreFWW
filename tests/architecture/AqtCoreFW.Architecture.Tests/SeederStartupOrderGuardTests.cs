using AqtCoreFW.Identity.Infrastructure;
using AqtCoreFW.Identity.Infrastructure.Seeding;
using AqtCoreFW.Organization.Infrastructure;
using AqtCoreFW.Organization.Infrastructure.Seeding;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Guards the cross-module seeder startup order fixed 2026-07-22 (the "dev 2-restart converge" bug).
///
/// <see cref="IHostedService.StartAsync"/> runs sequentially in DI registration order, and six
/// seeders form a dependency chain that spans the Identity and Organization modules:
/// <c>TenantSeeder → TenantBackfillSeeder → TenantRoleSeeder → OrgTreeSeeder → TenantUserSeeder
/// → TenantAssignmentSeeder</c> (TenantUserSeeder reads <c>organization.OrgUnits</c>;
/// TenantAssignmentSeeder reads <c>identity.AspNetUsers</c>; the canonical TenantSeeder must beat
/// the backfill's derived "TENANT-xxxx" fallback rows). Module-local registration cannot express
/// that interleaving — when each module registered its own seeders, TenantUserSeeder (Identity)
/// ran BEFORE OrgTreeSeeder (Organization) and graceful-skipped on first boot, requiring a
/// second restart.
///
/// The fix moved the AddHostedService calls to the API host (Program.cs), in explicit chain
/// order (all conditional on their Enabled flag except the always-on TenantBackfillSeeder).
/// These tests pin both halves of that contract:
/// <list type="number">
///   <item>Program.cs registers the seeders in chain order, before E2ETestDataSeeder
///         (which consumes their output). Text-parse, same shape as
///         <see cref="HangfireRegistrationConsistencyTests"/>.</item>
///   <item>The REAL module registration methods no longer register any of the six as hosted
///         services (re-adding one module-locally silently reintroduces the ordering bug), while
///         the seeder Options bindings stay module-owned — so the host's AddHostedService calls
///         are never left without options.</item>
/// </list>
/// </summary>
public sealed class SeederStartupOrderGuardTests
{
    /// <summary>The dependency chain, upstream first. Host must register in exactly this relative order.
    /// TenantBackfillSeeder sits right after TenantSeeder: it is the unconditional reactive fallback,
    /// and the canonical TenantSeeder must beat its derived "TENANT-xxxx" rows.</summary>
    private static readonly string[] ChainOrder =
    [
        nameof(TenantSeeder),
        nameof(TenantBackfillSeeder),
        nameof(TenantRoleSeeder),
        nameof(OrgTreeSeeder),
        nameof(TenantUserSeeder),
        nameof(TenantAssignmentSeeder),
    ];

    // ── Test 1: host Program.cs registers the chain in order, before E2ETestDataSeeder ──────

    [Fact]
    public void ApiProgram_Should_RegisterSeederChain_InDependencyOrder_BeforeE2ESeeder()
    {
        var programPath = Path.Combine(LocateRepoRoot(), "src", "host", "AqtCoreFW.Api", "Program.cs");
        var source = File.ReadAllText(programPath);

        // Index of each seeder's AddHostedService call in the source. Every one must be present:
        // a missing registration means the seeder silently reverted to module-local (or vanished).
        var positions = ChainOrder
            .Select(seeder => (Seeder: seeder, Index: source.IndexOf(
                $"AddHostedService<AqtCoreFW.{ModuleOf(seeder)}.Infrastructure.Seeding.{seeder}>",
                StringComparison.Ordinal)))
            .ToList();

        positions.Where(p => p.Index < 0).Select(p => p.Seeder).Should().BeEmpty(
            because: "each chained seeder must be host-registered in Program.cs — " +
                     "module-local registration cannot order across the Identity/Organization boundary");

        positions.Select(p => p.Index).Should().BeInAscendingOrder(
            because: "IHostedService.StartAsync runs in registration order and the chain is " +
                     string.Join(" → ", ChainOrder) + " — an inversion reintroduces the " +
                     "first-boot graceful-skip (dev 2-restart converge) bug");

        // E2ETestDataSeeder consumes the cluster's output (users, org units) — must register after it.
        var e2eIndex = source.IndexOf("AddHostedService<AqtCoreFW.Api.E2ETestDataSeeder>", StringComparison.Ordinal);
        e2eIndex.Should().BeGreaterThan(positions[^1].Index,
            because: "E2ETestDataSeeder reads data the seeder cluster produces, so it must be " +
                     "registered after TenantAssignmentSeeder");
    }

    // ── Test 2: real module registrations no longer hosted-register the chained seeders ─────

    [Fact]
    public void ModuleRegistrations_Should_NotHostedRegisterChainedSeeders_ButKeepBackfillAndOptions()
    {
        var services = new ServiceCollection();
        var config = BuildAllSeedersEnabledConfiguration();

        // Drive the REAL registration paths (not a hand-rolled copy) with every seed flag ON —
        // if a module ever re-adds a conditional AddHostedService for a chained seeder, this
        // configuration makes that branch fire and the assertion below catches it.
        // Development env name: the registration fail-fasts outside Development/Testing/Benchmark
        // (VNeID connector guard) — Development exercises the same seeder-relevant branches.
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);
        services.AddIdentityInfrastructure(config, env);
        services.AddOrganizationInfrastructure(config);

        var hostedImpls = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType?.Name)
            .Where(n => n is not null)
            .ToList();

        hostedImpls.Should().NotContain(ChainOrder,
            because: "all chained seeders (TenantBackfillSeeder included) are host-registered in " +
                     "explicit order; a module re-registering one locally would run it at the " +
                     "module's registration point and silently break the cross-module ordering");

        // Options bindings must remain module-owned, or the host's AddHostedService calls
        // would resolve seeders whose IOptions<T> silently bind to defaults.
        services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<TenantSeederOptions>));
        services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<TenantRoleSeederOptions>));
        services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<TenantUserSeederOptions>));
        services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<OrgTreeSeederOptions>));
        services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<TenantAssignmentSeederOptions>));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static string ModuleOf(string seeder) =>
        seeder is nameof(OrgTreeSeeder) or nameof(TenantAssignmentSeeder) ? "Organization" : "Identity";

    /// <summary>Config with every seed flag ON plus the connection-string stubs registration reads.</summary>
    private static IConfiguration BuildAllSeedersEnabledConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=(local);Database=stub;Integrated Security=true;TrustServerCertificate=true",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Redis:ConnectionString"] = "localhost:6379",
                // Identity registration fail-fasts on this at registration time (OpenIddict validation).
                ["Auth:AuthorityUrl"] = "http://localhost:5000",
                [$"{TenantSeederOptions.SectionName}:Enabled"] = "true",
                [$"{TenantRoleSeederOptions.SectionName}:Enabled"] = "true",
                [$"{TenantUserSeederOptions.SectionName}:Enabled"] = "true",
                [$"{OrgTreeSeederOptions.SectionName}:Enabled"] = "true",
                [$"{TenantAssignmentSeederOptions.SectionName}:Enabled"] = "true",
            })
            .Build();

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
