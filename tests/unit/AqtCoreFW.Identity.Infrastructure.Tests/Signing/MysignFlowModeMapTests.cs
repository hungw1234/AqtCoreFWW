using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for MysignFlowModeMap — validates mapping from admin-controlled SignatureFlowMode
/// to the provider-level MysignSignMode that drives the Mysign async field value.
/// </summary>
public sealed class MysignFlowModeMapTests
{
    [Fact]
    public void ToMysignSignMode_AsyncMode_MapsToAsyncPollDeferred()
    {
        var result = MysignFlowModeMap.ToMysignSignMode(SignatureFlowMode.Async);

        result.Should().Be(MysignSignMode.AsyncPollDeferred,
            because: "SignatureFlowMode.Async (admin setting) maps to async=2 poll-deferred");
    }

    [Fact]
    public void ToMysignSignMode_SyncMode_MapsToSync()
    {
        var result = MysignFlowModeMap.ToMysignSignMode(SignatureFlowMode.Sync);

        result.Should().Be(MysignSignMode.Sync,
            because: "SignatureFlowMode.Sync (admin setting) maps to async=0 blocking sync");
    }

    [Fact]
    public void ToMysignSignMode_AsyncCallbackMode_MapsToAsyncCallback()
    {
        var result = MysignFlowModeMap.ToMysignSignMode(SignatureFlowMode.AsyncCallback);

        result.Should().Be(MysignSignMode.AsyncCallback,
            because: "SignatureFlowMode.AsyncCallback (admin setting) maps to async=3 webhook callback");
    }

    [Fact]
    public void ToMysignSignMode_OutOfRangeValue_ThrowsArgumentOutOfRangeException()
    {
        // Direct cast to an unmapped enum value
        var unmappedValue = (SignatureFlowMode)999;

        var act = () => MysignFlowModeMap.ToMysignSignMode(unmappedValue);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("flowMode")
            .And.Message.Should().Contain("Unmapped SignatureFlowMode");
    }
}
