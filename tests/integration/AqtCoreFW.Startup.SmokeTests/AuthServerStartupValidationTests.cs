using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Architecture guard preventing AuthServer host from missing transitive DI registrations.
/// See <see cref="AuthServerHostFixture"/> for rationale and incident history.
///
/// Relocated 2026-05-09 from <c>tests/unit/AqtCoreFW.AuthServer.Tests/Startup/</c> per
/// AUTHSERVER-DI-ARCH-FU2 (smoke conceptually, not unit). Refactored to share
/// <see cref="HostDiArchGuard"/> with <c>ApiStartupSmokeTests</c> per FU3.
/// </summary>
// [Collection] forces serial execution with other smoke tests — see comment on
// ApiControllerDiResolutionSmokeTests for env-var-race rationale.
[Collection(SmokeTestCollection.CollectionName)]
public sealed class AuthServerStartupValidationTests
    : IClassFixture<AuthServerHostFixture>
{
    private readonly AuthServerHostFixture _fixture;

    public AuthServerStartupValidationTests(AuthServerHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void AuthServer_Host_Builds_AndExplicitServicesResolve()
    {
        // CreateClient triggers WebApplicationFactory host-build pipeline, which executes
        // builder.Build() and the post-Build block:
        //   * GetRequiredService<JobStorage>()                    → Hangfire registration
        //   * GetRequiredService<IPasswordVerifyTimingDecoy>()    → BE-LOGIN-TIMING-SIDE-CHANNEL
        //   * GetRequiredService<IdentityDbContext>()             → Identity infrastructure
        //   * GetRequiredService<MessagingDbContext>()            → Messaging dead-letter store
        // Any missing registration in this chain throws InvalidOperationException here.
        var client = _fixture.CreateClient();
        client.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void AuthServer_AllControllers_AreConstructableViaDi()
    {
        _fixture.CreateClient();

        using var scope = _fixture.Services.CreateScope();

        // FU4 (2026-05-09): walks ApplicationPartManager — equivalent to host-only
        // enumeration here because AuthServer has no AddApplicationPart calls (all
        // controllers are host-local). Same API as Api host for symmetry; future
        // module wiring would be picked up automatically.
        var controllers = HostDiArchGuard.EnumerateAllControllers(scope.ServiceProvider);

        controllers.Should().NotBeEmpty(
            "AuthServer host must expose controllers via ApplicationPartManager; if " +
            "this assertion fires, AddControllersWithViews wiring is broken.");

        var failures = HostDiArchGuard.TryResolveControllers(scope.ServiceProvider, controllers);

        failures.Should().BeEmpty(
            "every AuthServer controller must be constructable via DI. " +
            "Missing transitive deps are silent in production until first request " +
            "(precedents: IMessageBus v2.133p3, IGuidGenerator 2026-05-08).\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// The only assertion that the config → <c>SpaClientRedirectUris.Resolve</c> → descriptor →
    /// OpenIddict store chain actually runs. The resolver's own unit tests stay green even if
    /// <c>SeedApplicationsAsync</c> never calls it, so this test is what makes that wiring real.
    /// <para>
    /// The fixture boots with <c>UseEnvironment("Development")</c> and the committed
    /// <c>appsettings.Development.json</c> deliberately carries no <c>Auth:SpaClient</c> section,
    /// so this covers the fallback branch — the path the standard dev loop, the Playwright e2e
    /// specs, and Swagger all rely on. Keep that section out of the committed dev config: adding
    /// it would silently re-point this test at the configured branch while its assertions still
    /// read like fallback coverage.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "Smoke")]
    public async Task AuthServer_SpaClient_IsSeeded_WithDevelopmentFallbackRedirectUris()
    {
        _fixture.CreateClient();

        using var scope = _fixture.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var spaClient = await manager.FindByClientIdAsync("aqtcorefw-spa-dev");
        spaClient.Should().NotBeNull("SeedApplicationsAsync must register the SPA client on boot");

        var redirectUris = await manager.GetRedirectUrisAsync(spaClient!);
        redirectUris.Should()
            .Contain("http://localhost:5173/callback", "the Vite dev origin drives the e2e specs").And
            .Contain(
                "http://localhost:5173/silent-renew.html",
                "oidc-client-ts loads this in a hidden iframe on refresh-token renew; " +
                "an unwhitelisted URI yields 400 invalid_request");

        // One entry per origin, stored in whichever spelling the fallback listed FIRST.
        // new Uri("http://h:p") equals new Uri("http://h:p/"), so the descriptor's ISet<Uri>
        // keeps a single element, and OpenIddict persists that element's OriginalString — it
        // does NOT normalize. Logout matching is then an ordinal string compare, so the stored
        // value must be the slashless form the SPA sends as window.location.origin.
        var postLogoutUris = await manager.GetPostLogoutRedirectUrisAsync(spaClient!);
        postLogoutUris.Should().Contain("http://localhost:5173");
    }
}
