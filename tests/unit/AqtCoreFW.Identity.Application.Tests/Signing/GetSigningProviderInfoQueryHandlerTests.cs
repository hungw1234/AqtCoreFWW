using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Signing;

/// <summary>
/// Tests for GetSigningProviderInfoQueryHandler — the non-admin provider discovery the
/// form-fill signing modal depends on. Covers: enabled config resolves capabilities,
/// disabled/missing config reports unavailable, and a configured-but-unregistered provider
/// degrades to enabled-with-no-capabilities rather than throwing.
/// </summary>
public sealed class GetSigningProviderInfoQueryHandlerTests
{
    private readonly ITenantSignatureConfigRepository _configRepository = Substitute.For<ITenantSignatureConfigRepository>();
    private readonly ISignatureProviderResolver _resolver = Substitute.For<ISignatureProviderResolver>();
    private readonly GetSigningProviderInfoQueryHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    public GetSigningProviderInfoQueryHandlerTests()
    {
        _sut = new GetSigningProviderInfoQueryHandler(_configRepository, _resolver);
    }

    private static TenantSignatureConfig Config(string providerKey, bool isEnabled) =>
        TenantSignatureConfig.Create(TenantId, providerKey, null, string.Empty, OcspMode.Soft, isEnabled, SignatureFlowMode.Async, Guid.NewGuid());

    [Fact]
    public async Task EnabledConfig_ResolvesProviderCapabilities()
    {
        _configRepository.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Config("usb-vgca", isEnabled: true));
        var provider = Substitute.For<IDigitalSignatureProvider>();
        provider.Capabilities.Returns(SignatureProviderCapabilities.RawSign | SignatureProviderCapabilities.RequiresLocalAgent);
        _resolver.Resolve("usb-vgca").Returns(provider);

        var result = await _sut.Handle(new GetSigningProviderInfoQuery(TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();
        result.Value.ProviderKey.Should().Be("usb-vgca");
        result.Value.Capabilities.Should().Be(
            SignatureProviderCapabilities.RawSign | SignatureProviderCapabilities.RequiresLocalAgent);
    }

    [Fact]
    public async Task NoConfig_ReportsDisabled()
    {
        _configRepository.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((TenantSignatureConfig?)null);

        var result = await _sut.Handle(new GetSigningProviderInfoQuery(TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();
        result.Value.ProviderKey.Should().BeNull();
        result.Value.Capabilities.Should().Be(SignatureProviderCapabilities.None);
        _resolver.DidNotReceive().Resolve(Arg.Any<string>());
    }

    [Fact]
    public async Task DisabledConfig_ReportsDisabled()
    {
        _configRepository.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Config("usb-vgca", isEnabled: false));

        var result = await _sut.Handle(new GetSigningProviderInfoQuery(TenantId), CancellationToken.None);

        result.Value.IsEnabled.Should().BeFalse();
        result.Value.ProviderKey.Should().BeNull();
    }

    [Fact]
    public async Task UnregisteredProvider_DegradesToNoCapabilities_DoesNotThrow()
    {
        _configRepository.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Config("ghost-provider", isEnabled: true));
        _resolver.Resolve("ghost-provider").Returns(_ => throw new KeyNotFoundException());

        var result = await _sut.Handle(new GetSigningProviderInfoQuery(TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();
        result.Value.ProviderKey.Should().Be("ghost-provider");
        result.Value.Capabilities.Should().Be(SignatureProviderCapabilities.None);
    }
}
