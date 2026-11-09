using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// OFF-branch proof for the deferred-module config flags (v1.0 launch posture): boots the
/// real Api host through Program.cs with all five <c>Features:{Name}:Enabled=false</c> and
/// asserts each layer of the gate actually engaged. The ON branch (default, missing keys)
/// is covered by the existing suite — every module test and
/// <c>ApiControllerDiResolutionSmokeTests</c> runs with flags absent, i.e. enabled.
///
/// Drives the REAL registration path (WebApplicationFactory over Program.cs), not a
/// hand-rolled ServiceCollection — hand-rolled setup would bypass the very wiring under
/// test. The fixture's own boot already proves the migration filter works: with the five
/// DbContexts unregistered, an unfiltered migration loop would throw on resolve.
/// </summary>
[Collection(SmokeTestCollection.CollectionName)]
[Trait("Category", "Smoke")]
public sealed class DeferredModulesOffSmokeTests : IClassFixture<DeferredModulesOffHostFixture>
{
    private readonly DeferredModulesOffHostFixture _fixture;

    public DeferredModulesOffSmokeTests(DeferredModulesOffHostFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// One representative route per disabled module must 404 — not 401/403/500. Each route is a
    /// REAL GET action on the module's controller (verified against [HttpGet] attributes), so with
    /// the module ON it would NOT 404 — a bare controller prefix with no parameterless GET would
    /// 404 in both states and prove nothing (caught by mutation testing 2026-07-23).
    /// </summary>
    [Theory, Trait("Category", "Smoke")]
    [InlineData("/api/v1/ai/chat/1/history")]                                          // Ai
    [InlineData("/api/v1/conversations")]                                              // Collaboration
    [InlineData("/api/v1/business-processes")]                                         // BusinessProcess
    [InlineData("/api/v1/reports/definitions")]                                        // Reporting
    [InlineData("/api/v1/rules/rule-sets")]                                            // Rules
    public async Task DisabledModule_RepresentativeEndpoint_Returns404(string route)
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync(route);

        // 404 proves the route is NOT MAPPED (Presentation part removed). A 401/403 would mean
        // the controller was discovered and auth middleware engaged; a 500 would mean it was
        // discovered but its handlers were unresolvable — the exact half-off state the
        // ApplicationPartManager half of the gate exists to prevent.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"a disabled module's endpoint {route} must not be mapped at all");
    }

    /// <summary>
    /// The flags endpoint itself must SURVIVE the all-off posture — it lives in the host assembly
    /// precisely so the FE can still ask "what is enabled" when every deferred module is not.
    /// 401 (mapped, auth required) distinguishes it from the 404 of removed module routes.
    /// </summary>
    [Fact, Trait("Category", "Smoke")]
    public async Task ModuleFlagsEndpoint_StaysMapped_WithAllModulesOff()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync("/api/v1/system/module-flags");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the host-local flags endpoint must stay mapped (auth-gated) while module routes 404");
    }

    [Fact, Trait("Category", "Smoke")]
    public void DisabledModules_PresentationParts_AreRemovedFromDiscovery()
    {
        _fixture.CreateClient();
        var partManager = _fixture.Services.GetRequiredService<ApplicationPartManager>();

        var partNames = partManager.ApplicationParts.Select(p => p.Name).ToList();

        foreach (var moduleName in AqtCoreFW.Api.DeferredModuleFlags.ModuleNames)
        {
            partNames.Should().NotContain(
                name => name.StartsWith($"AqtCoreFW.{moduleName}.Presentation", StringComparison.Ordinal),
                $"module {moduleName} is disabled — its Presentation assembly must be removed from controller discovery");
        }
    }

    [Fact, Trait("Category", "Smoke")]
    public void DisabledModules_DbContexts_AreNotRegistered()
    {
        _fixture.CreateClient();
        using var scope = _fixture.Services.CreateScope();

        foreach (var (moduleName, contextType) in AqtCoreFW.Api.DeferredModuleFlags.ModuleDbContexts)
        {
            scope.ServiceProvider.GetService(contextType).Should().BeNull(
                $"module {moduleName} is disabled — its DbContext must not be registered "
                + "(its registration block in Program.cs must have been skipped)");
        }
    }

    /// <summary>
    /// Disabled modules must contribute zero hosted services (e.g. Rules'
    /// RulesEngineCacheSubscriber). Asserted against the PRE-strip capture — the fixture
    /// strips IHostedService for boot economics, which would otherwise make this vacuous.
    /// Non-vacuity of the capture itself: the host always registers unrelated hosted
    /// services (outbox dispatcher, seeders), so an empty capture means the capture broke.
    /// </summary>
    [Fact, Trait("Category", "Smoke")]
    public void DisabledModules_RegisterNoHostedServices()
    {
        _fixture.CreateClient();

        _fixture.RegisteredHostedServiceImplementations.Should().NotBeEmpty(
            "the capture must have seen the host's own hosted services; empty means the capture is broken");

        var moduleAssemblyPrefixes = AqtCoreFW.Api.DeferredModuleFlags.ModuleNames
            .Select(name => $"AqtCoreFW.{name}.")
            .ToList();

        _fixture.RegisteredHostedServiceImplementations
            .Where(impl => moduleAssemblyPrefixes.Any(p => impl.StartsWith(p, StringComparison.Ordinal)))
            .Should().BeEmpty("disabled modules must not register hosted services");
    }
}
