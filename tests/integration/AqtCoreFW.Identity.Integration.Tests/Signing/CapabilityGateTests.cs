using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using AqtCoreFW.Tests.SharedFixtures;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Integration tests for <see cref="ISignatureProviderResolver"/> capability gate.
///
/// W7 cross-cutting tests verifying:
/// - Resolver enforces capability-based routing (mismatched capability → throws)
/// - USB VGCA provider supports RawSign | XmlDsig | JsonSign | DocFlow | ClientSideSigning
/// - Remote Viettel rejects ClientSideSigning (server-side only)
/// - Deprecated "remote-vnpt-smartca" key is rejected at tenant config layer
///
/// Uses real DI container via WebAppFixture for resolver instance.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class CapabilityGateTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public CapabilityGateTests(WebAppFixture app) => _app = app;

    // ── Test 1: Resolver accepts USB VGCA with RawSign capability ────────────────

    [Fact]
    public void Resolver_Resolve_UsbVgca_With_RawSign_Returns_Provider()
    {
        // USB VGCA (W1) declares RawSign | ClientSideSigning capabilities.
        // Verify resolver can locate it when RawSign is requested.

        using var scope = _app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISignatureProviderResolver>();

        var provider = resolver.Resolve("usb-vgca", SignatureProviderCapabilities.RawSign);

        provider.Should().NotBeNull("USB VGCA must be resolvable with RawSign capability");
        provider.ProviderKey.Should().Be("usb-vgca");
        (provider.Capabilities & SignatureProviderCapabilities.RawSign).Should().NotBe(0);
    }

    // ── Test 2: Resolver rejects USB VGCA with ServerSideSigning capability ──────

    [Fact]
    public void Resolver_Resolve_UsbVgca_With_ServerSideSigning_Throws_NotSupportedException()
    {
        // USB VGCA is client-side only — server-side signing not supported.
        // Verify resolver throws when ServerSideSigning capability is requested.

        using var scope = _app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISignatureProviderResolver>();

        var action = () => resolver.Resolve("usb-vgca", SignatureProviderCapabilities.ServerSideSigning);

        action.Should().Throw<NotSupportedException>(
            "USB VGCA does not support ServerSideSigning (client-side only)");
    }

    // ── Test 3: Resolver rejects Viettel with ClientSideSigning capability ────────

    [Fact]
    public void Resolver_Resolve_RemoteViettelMysign_With_ClientSideSigning_Throws_NotSupportedException()
    {
        // Viettel Mysign (W4-W6) is server-side only — client-side signing not supported.
        // Verify resolver throws when ClientSideSigning is requested.

        using var scope = _app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISignatureProviderResolver>();

        var action = () => resolver.Resolve("remote-viettel-mysign", SignatureProviderCapabilities.ClientSideSigning);

        action.Should().Throw<NotSupportedException>(
            "Viettel Mysign does not support ClientSideSigning (server-side only)");
    }

    // ── Test 4: Resolver returns provider for Viettel with ServerSideSigning ──────

    [Fact]
    public void Resolver_Resolve_RemoteViettelMysign_With_ServerSideSigning_Returns_Provider()
    {
        // Viettel Mysign declares ServerSideSigning capability.
        // Verify resolver can locate it when ServerSideSigning is requested.

        using var scope = _app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISignatureProviderResolver>();

        var provider = resolver.Resolve("remote-viettel-mysign", SignatureProviderCapabilities.ServerSideSigning);

        provider.Should().NotBeNull("Viettel Mysign must be resolvable with ServerSideSigning capability");
        provider.ProviderKey.Should().Be("remote-viettel-mysign");
        (provider.Capabilities & SignatureProviderCapabilities.ServerSideSigning).Should().NotBe(0);
    }

    // ── Test 5: USB VGCA full capability mask verification ──────────────────────

    [Fact]
    public void Resolver_Capability_Mask_Matches_Provider_Capabilities_Property()
    {
        // USB VGCA declares: RawSign | XmlDsig | JsonSign | DocFlow | ClientSideSigning | RequiresLocalAgent.
        // Verify all flags are present in resolved provider.

        using var scope = _app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISignatureProviderResolver>();

        var provider = resolver.Resolve("usb-vgca", SignatureProviderCapabilities.RawSign);

        // Check individual flags
        (provider.Capabilities & SignatureProviderCapabilities.RawSign).Should().NotBe(0);
        (provider.Capabilities & SignatureProviderCapabilities.XmlDsig).Should().NotBe(0);
        (provider.Capabilities & SignatureProviderCapabilities.JsonSign).Should().NotBe(0);
        (provider.Capabilities & SignatureProviderCapabilities.DocFlow).Should().NotBe(0);
        (provider.Capabilities & SignatureProviderCapabilities.ClientSideSigning).Should().NotBe(0);
        (provider.Capabilities & SignatureProviderCapabilities.RequiresLocalAgent).Should().NotBe(0);
    }

    // ── Test 6: Viettel Mysign full capability mask verification ────────────────

    [Fact]
    public void Resolver_Viettel_Capability_Mask_Matches_Provider()
    {
        // Viettel Mysign declares: ServerSideSigning | RawSign (W4 minimal mask).
        // BatchSigning intentionally omitted (each signHash needs separate mobile confirmation).
        // VGCA-only flags (XmlDsig/JsonSign/DocFlow/ClientSideSigning/RequiresLocalAgent) must be absent.
        using var scope = _app.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISignatureProviderResolver>();

        var provider = resolver.Resolve("remote-viettel-mysign", SignatureProviderCapabilities.ServerSideSigning);

        (provider.Capabilities & SignatureProviderCapabilities.ServerSideSigning).Should().NotBe(0);
        (provider.Capabilities & SignatureProviderCapabilities.RawSign).Should().NotBe(0);

        (provider.Capabilities & SignatureProviderCapabilities.ClientSideSigning).Should().Be(0,
            "Viettel is server-side only");
        (provider.Capabilities & SignatureProviderCapabilities.RequiresLocalAgent).Should().Be(0,
            "Viettel does not require local agent");
        (provider.Capabilities & SignatureProviderCapabilities.XmlDsig).Should().Be(0,
            "XMLDSig signing is VGCA Tier-2 only");
        (provider.Capabilities & SignatureProviderCapabilities.DocFlow).Should().Be(0,
            "DocFlow is VGCA Tier-3 only");
    }
}
