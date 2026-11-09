using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="SignatureProviderResolver"/>.
/// Tests keyed DI lookup, missing-key error, and GetAvailableProviders enumeration.
/// </summary>
public sealed class SignatureProviderResolverTests
{
    private static IDigitalSignatureProvider MakeProvider(string key)
    {
        var p = Substitute.For<IDigitalSignatureProvider>();
        p.ProviderKey.Returns(key);
        p.DisplayName.Returns($"Provider {key}");
        p.Capabilities.Returns(SignatureProviderCapabilities.ServerSideSigning);
        return p;
    }

    private static SignatureProviderResolver BuildResolver(
        IEnumerable<(string key, IDigitalSignatureProvider provider)> registrations)
    {
        var services = new ServiceCollection();
        foreach (var (key, provider) in registrations)
        {
            services.AddKeyedSingleton<IDigitalSignatureProvider>(key, provider);
            services.AddSingleton<IDigitalSignatureProvider>(provider); // unkeyed for enumeration
        }
        var sp = services.BuildServiceProvider();
        var allProviders = sp.GetServices<IDigitalSignatureProvider>();
        return new SignatureProviderResolver(sp, allProviders);
    }

    // ── Resolve: known key returns provider ──────────────────────────────────

    [Fact]
    public void Resolve_KnownKey_ReturnsCorrectProvider()
    {
        var remote = MakeProvider("remote-viettel-mysign");
        var resolver = BuildResolver([("remote-viettel-mysign", remote)]);

        var result = resolver.Resolve("remote-viettel-mysign");

        result.Should().BeSameAs(remote);
    }

    [Fact]
    public void Resolve_UsbKey_ReturnsUsbProvider()
    {
        var usb = MakeProvider("usb-vgca");
        var resolver = BuildResolver([("usb-vgca", usb)]);

        var result = resolver.Resolve("usb-vgca");

        result.Should().BeSameAs(usb);
    }

    // ── Resolve: unknown key throws KeyNotFoundException ──────────────────────

    [Fact]
    public void Resolve_UnknownKey_ThrowsKeyNotFoundException()
    {
        var resolver = BuildResolver([]); // no providers registered

        var act = () => resolver.Resolve("unknown-provider");

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*unknown-provider*");
    }

    // ── GetAvailableProviders: returns all registered known-key providers ─────

    [Fact]
    public void GetAvailableProviders_BothRegistered_ReturnsBoth()
    {
        var remote = MakeProvider("remote-viettel-mysign");
        var usb = MakeProvider("usb-vgca");
        var resolver = BuildResolver([
            ("remote-viettel-mysign", remote),
            ("usb-vgca", usb)
        ]);

        var result = resolver.GetAvailableProviders();

        result.Should().HaveCount(2);
        result.Select(p => p.ProviderKey).Should().Contain("remote-viettel-mysign");
        result.Select(p => p.ProviderKey).Should().Contain("usb-vgca");
    }

    [Fact]
    public void GetAvailableProviders_NoneRegistered_ReturnsEmpty()
    {
        var resolver = BuildResolver([]);

        var result = resolver.GetAvailableProviders();

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableProviders_OneRegistered_ReturnsOne()
    {
        var usb = MakeProvider("usb-vgca");
        var resolver = BuildResolver([("usb-vgca", usb)]);

        var result = resolver.GetAvailableProviders();

        result.Should().HaveCount(1);
        result[0].ProviderKey.Should().Be("usb-vgca");
    }
}
