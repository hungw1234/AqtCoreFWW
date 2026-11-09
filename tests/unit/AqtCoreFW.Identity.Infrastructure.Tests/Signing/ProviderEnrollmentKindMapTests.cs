using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using FluentAssertions;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Covers the concrete key→kind table (the guard unit tests mock the abstraction). The arch drift test proves
/// every REGISTERED provider is mapped; these assert the mapping VALUES + fail-closed miss + case-insensitivity.
/// </summary>
public sealed class ProviderEnrollmentKindMapTests
{
    private readonly ProviderEnrollmentKindMap _map = new();

    [Theory]
    [InlineData("usb-vgca", EnrollmentKind.UsbTokenVgca)]
    [InlineData("remote-viettel-mysign", EnrollmentKind.RemoteViettelMysign)]
    public void TryResolve_MappedKey_ReturnsTrueAndKind(string providerKey, EnrollmentKind expected)
    {
        _map.TryResolve(providerKey, out var kind).Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData("USB-VGCA", EnrollmentKind.UsbTokenVgca)]
    [InlineData("Remote-Viettel-MySign", EnrollmentKind.RemoteViettelMysign)]
    public void TryResolve_IsCaseInsensitive(string providerKey, EnrollmentKind expected)
    {
        _map.TryResolve(providerKey, out var kind).Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData("remote-vnpt-smartca")]  // deprecated/not-yet-revived — must NOT map (fail-closed)
    [InlineData("no-such-provider")]
    [InlineData("")]
    public void TryResolve_UnmappedKey_ReturnsFalse(string providerKey)
    {
        _map.TryResolve(providerKey, out _).Should().BeFalse();
    }

    [Fact]
    public void MappedKeys_ContainsExactlyTheTwoLiveProviders()
    {
        _map.MappedKeys.Should().BeEquivalentTo(["usb-vgca", "remote-viettel-mysign"]);
    }
}
