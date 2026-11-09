using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AqtCoreFW.AuthServer.Tests;

/// <summary>
/// Tests for <see cref="SpaClientRedirectUris.Resolve"/> — the OpenIddict SPA client's
/// redirect-URI source. Development falls back to localhost so the dev loop needs no config;
/// outside Development a missing or blank value must THROW, because the seeder runs in every
/// environment and a localhost-only client fails every authorize round-trip with an opaque
/// redirect_uri mismatch instead of a loud startup error.
/// </summary>
public sealed class SpaClientRedirectUriResolverTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    // ── Development: fallback branch ───────────────────────────────────────

    [Fact]
    public void Development_NoConfig_ReturnsLocalhostFallback()
    {
        var uris = SpaClientRedirectUris.Resolve(
            Config(),
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment: true);

        // The e2e spec (web/e2e/authserver-authorize-flow.spec.ts) and oidc-client-ts silent
        // renew both depend on these exact URIs surviving the move to config.
        uris.Select(u => u.ToString()).Should()
            .Contain("http://localhost:5173/callback").And
            .Contain("http://localhost:5173/silent-renew.html");
    }

    [Fact]
    public void Development_EmptyStringEntries_StillReturnsFallback()
    {
        // Committed configs ship values as empty strings — a bare `?? fallback` would treat
        // "" as a deliberate choice and seed a client with zero valid callbacks.
        var uris = SpaClientRedirectUris.Resolve(
            Config(
                ($"{SpaClientRedirectUris.RedirectUrisKey}:0", ""),
                ($"{SpaClientRedirectUris.RedirectUrisKey}:1", "   ")),
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment: true);

        uris.Should().HaveCount(SpaClientRedirectUris.DevelopmentRedirectUris.Length);
        uris.Select(u => u.ToString()).Should().Contain("http://localhost:5173/callback");
    }

    [Fact]
    public void Development_ConfigPresent_OverridesFallback()
    {
        var uris = SpaClientRedirectUris.Resolve(
            Config(($"{SpaClientRedirectUris.RedirectUrisKey}:0", "https://app.example.gov.vn/callback")),
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment: true);

        // Configured values REPLACE the fallback — they are not merged with it.
        uris.Should().ContainSingle()
            .Which.ToString().Should().Be("https://app.example.gov.vn/callback");
    }

    // ── Non-Development: fail-fast branch ──────────────────────────────────

    [Fact]
    public void NonDevelopment_NoConfig_Throws_NamingTheConfigKey()
    {
        var act = () => SpaClientRedirectUris.Resolve(
            Config(),
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:SpaClient:RedirectUris*");
    }

    [Fact]
    public void NonDevelopment_AllWhitespaceEntries_Throws_NamingTheConfigKey()
    {
        // The delta from the case above: config is PRESENT but semantically empty. A blank
        // Helm value or an unsubstituted placeholder must fail the same way an absent key does.
        var act = () => SpaClientRedirectUris.Resolve(
            Config(
                ($"{SpaClientRedirectUris.RedirectUrisKey}:0", ""),
                ($"{SpaClientRedirectUris.RedirectUrisKey}:1", "  ")),
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:SpaClient:RedirectUris*");
    }

    [Fact]
    public void NonDevelopment_ConfigPresent_ReturnsConfiguredUris()
    {
        var uris = SpaClientRedirectUris.Resolve(
            Config(
                ($"{SpaClientRedirectUris.RedirectUrisKey}:0", "http://app.aqt.localtest.me/callback"),
                ($"{SpaClientRedirectUris.RedirectUrisKey}:1", "http://app.aqt.localtest.me/silent-renew.html")),
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment: false);

        uris.Select(u => u.ToString()).Should().BeEquivalentTo(
            "http://app.aqt.localtest.me/callback",
            "http://app.aqt.localtest.me/silent-renew.html");
    }

    // ── Validation applies in every environment ────────────────────────────

    [Fact]
    public void RelativeUri_Throws_NamingTheOffendingValue()
    {
        // isDevelopment: true on purpose — a typo in a tunnel URL must be loud, not silently
        // dropped from the whitelist and rediscovered as a login failure.
        var act = () => SpaClientRedirectUris.Resolve(
            Config(($"{SpaClientRedirectUris.RedirectUrisKey}:0", "/callback")),
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*/callback*");
    }

    [Fact]
    public void NonHttpScheme_Throws_NamingTheOffendingValue()
    {
        // A file:// URI is absolute yet not a valid SPA redirect. On Linux "/callback" itself parses
        // as file:///callback, so the scheme guard (not just UriKind.Absolute) is what rejects it —
        // asserted here explicitly so the invariant holds independently of the host OS's URI parsing.
        var act = () => SpaClientRedirectUris.Resolve(
            Config(($"{SpaClientRedirectUris.RedirectUrisKey}:0", "file:///callback")),
            SpaClientRedirectUris.RedirectUrisKey,
            SpaClientRedirectUris.DevelopmentRedirectUris,
            isDevelopment: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*file:///callback*");
    }

    // ── Post-logout spelling invariant ─────────────────────────────────────

    [Fact]
    public void PostLogoutFallback_StoresSlashlessOrigin_AsOidcClientsSendIt()
    {
        // Regression guard. new Uri("http://h:p") equals new Uri("http://h:p/"), so the seeder's
        // ISet<Uri> keeps only whichever spelling was inserted FIRST, and OpenIddict persists that
        // one's OriginalString. Logout matching is an ordinal string compare (the relaxed
        // URI-parsing path is native-clients-only; this client is Public), and oidc-client-ts
        // sends window.location.origin — always slashless. List the slash form first and every
        // logout fails with 400 invalid_request.
        var uris = SpaClientRedirectUris.Resolve(
            Config(),
            SpaClientRedirectUris.PostLogoutRedirectUrisKey,
            SpaClientRedirectUris.DevelopmentPostLogoutRedirectUris,
            isDevelopment: true);

        var stored = new HashSet<Uri>();
        stored.UnionWith(uris);

        stored.Select(u => u.OriginalString).Should().Contain(
            "http://localhost:5173",
            "the SPA sends window.location.origin, which OpenIddict compares with StringComparison.Ordinal");
    }

    [Fact]
    public void PostLogoutKey_Throws_NamingItsOwnKey()
    {
        // The two Resolve calls in the seeder are near-identical; this guards a copy-paste
        // key mix-up that would otherwise surface as a misleading error message.
        var act = () => SpaClientRedirectUris.Resolve(
            Config(),
            SpaClientRedirectUris.PostLogoutRedirectUrisKey,
            SpaClientRedirectUris.DevelopmentPostLogoutRedirectUris,
            isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:SpaClient:PostLogoutRedirectUris*");
    }
}
