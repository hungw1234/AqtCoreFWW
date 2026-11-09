using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Architecture guard preventing the Api host from missing transitive DI registrations
/// for any controller. Mirrors <see cref="AuthServerStartupValidationTests"/> for Api side.
///
/// Why this is needed despite Api having <c>ValidateOnBuild=true</c>: controllers are NOT
/// registered in the DI container. They are constructed per-request via
/// <see cref="ActivatorUtilities.CreateInstance"/>, so missing transitive deps in a
/// controller's constructor chain only fail on first HTTP call. ValidateOnBuild only
/// validates explicitly registered services, not on-demand-constructed types.
///
/// Incident precedents:
///   * v2.133p3 (2026-04-28): IMessageBus missing → AuthServer /connect/authorize 500
///   * 2026-05-08:           IGuidGenerator missing → PHASE7-TIER-BC-VALIDATION miss
///
/// Closes AUTHSERVER-DI-ARCH-FU1 (P3) backlog row.
///
/// Uses dedicated <see cref="ApiHostFixture"/> (own Testcontainer) instead of
/// <see cref="SeederSmokeFixture"/> because the seeder fixture's shared SQL DB causes
/// BtcIdentitySeeder validation collisions across tests.
/// </summary>
// [Collection] shares the SqlServerFixture and caps concurrent Testcontainer SQL instances.
// Connection strings are injected per-fixture via builder.UseSetting (UseTestConnectionStrings),
// not a process-global env var, so hosts no longer race a shared slot regardless of ordering.
[Collection(SmokeTestCollection.CollectionName)]
[Trait("Category", "Smoke")]
public sealed class ApiControllerDiResolutionSmokeTests
    : IClassFixture<ApiHostFixture>
{
    private readonly ApiHostFixture _fixture;

    public ApiControllerDiResolutionSmokeTests(ApiHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact, Trait("Category", "Smoke")]
    public void Api_AllControllers_AreConstructableViaDi()
    {
        _fixture.CreateClient();

        using var scope = _fixture.Services.CreateScope();

        // FU4 (2026-05-09): walks ApplicationPartManager → covers host-local Admin
        // controllers AND every module Presentation assembly wired via AddApplicationPart
        // (Identity/Forms/Workflow/Notifications/Audit/MasterData/SystemParams/Organization/
        // Ai/Files/Reporting/Rules/Collaboration). Pre-FU4 only host assembly was scanned,
        // module controllers were validated only transitively through host-local deps.
        var controllers = HostDiArchGuard.EnumerateAllControllers(scope.ServiceProvider);

        controllers.Should().NotBeEmpty(
            "Api host must expose controllers via ApplicationPartManager; if this " +
            "assertion fires, AddControllers/AddApplicationPart wiring is broken.");

        var failures = HostDiArchGuard.TryResolveControllers(scope.ServiceProvider, controllers);

        failures.Should().BeEmpty(
            "every Api controller (host + module Presentation assemblies) must be " +
            "constructable via DI. Missing transitive deps are silent in production " +
            "until first request (precedents: IMessageBus v2.133p3, IGuidGenerator " +
            "2026-05-08).\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// Per-module controller PRESENCE guard. <see cref="Api_AllControllers_AreConstructableViaDi"/>
    /// only proves the discovered set resolves — it stays green even if an entire module's controllers
    /// vanish from discovery (it asserts no specific module is present). This guard asserts every module
    /// Presentation assembly contributes ≥1 routed controller, so dropping a module's wiring fails loudly.
    ///
    /// Load-bearing for the AddApplicationPart cleanup (2026-06-21): module Presentation controllers are
    /// discovered via ASP.NET Core auto-discovery (DependencyContext) for project-referenced assemblies —
    /// empirically proven by Integration + BusinessProcess routing 200 with NO explicit AddApplicationPart.
    /// This guard is the safety net: it must stay green after the redundant AddApplicationPart calls are
    /// removed from Program.cs, proving auto-discovery alone surfaces every module.
    /// </summary>
    [Fact, Trait("Category", "Smoke")]
    public void Api_EveryModulePresentation_ContributesAtLeastOneController()
    {
        _fixture.CreateClient();
        using var scope = _fixture.Services.CreateScope();

        var discoveredAssemblies = HostDiArchGuard
            .EnumerateAllControllers(scope.ServiceProvider)
            .Select(t => t.Assembly.GetName().Name)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ExpectedModulePresentationAssemblies
            .Where(name => !discoveredAssemblies.Contains(name))
            .ToList();

        missing.Should().BeEmpty(
            "every module Presentation assembly referenced by the Api host must contribute at least one " +
            "controller to ASP.NET Core routing (via AddApplicationPart OR DependencyContext auto-discovery). " +
            "A module here with zero discovered controllers means its endpoints 404 in production. " +
            "Modules with no routed controller: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The 15 module Presentation assemblies project-referenced by <c>AqtCoreFW.Api</c>. Kept explicit
    /// (not reflected from the host) so this is a deliberate contract: shipping a new module's controllers
    /// requires adding it here, forcing a conscious "is this routed?" check. Host-local admin controllers
    /// live in the entry assembly (always the primary ApplicationPart) and need no entry.
    /// </summary>
    private static readonly IReadOnlyList<string> ExpectedModulePresentationAssemblies =
    [
        "AqtCoreFW.Identity.Presentation",
        "AqtCoreFW.Rules.Presentation",
        "AqtCoreFW.Collaboration.Presentation",
        "AqtCoreFW.Ai.Presentation",
        "AqtCoreFW.Audit.Presentation",
        "AqtCoreFW.Files.Presentation",
        "AqtCoreFW.Forms.Presentation",
        "AqtCoreFW.Notifications.Presentation",
        "AqtCoreFW.Reporting.Presentation",
        "AqtCoreFW.Workflow.Presentation",
        "AqtCoreFW.Integration.Presentation",
        "AqtCoreFW.MasterData.Presentation",
        "AqtCoreFW.Organization.Presentation",
        "AqtCoreFW.SystemParams.Presentation",
        "AqtCoreFW.BusinessProcess.Presentation",
    ];
}
