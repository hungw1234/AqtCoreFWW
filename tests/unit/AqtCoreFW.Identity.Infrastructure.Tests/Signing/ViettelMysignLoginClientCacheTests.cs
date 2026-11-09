using System.Net;
using System.Text;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Regression guard for the token-cache write. The Api host registers IMemoryCache with a SizeLimit,
/// and a Set() without an entry Size throws InvalidOperationException — which crashed the real sign
/// flow at login ("Cache entry must specify a value for Size when SizeLimit is set"). The WireMock
/// integration tests miss this because their fixture cache has no SizeLimit. This test pins the fix by
/// using a size-limited cache, exactly like the host.
/// </summary>
public sealed class ViettelMysignLoginClientCacheTests
{
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static IOptionsMonitor<ViettelMysignOptions> StubOptions()
    {
        var opts = new ViettelMysignOptions
        {
            BaseUrl = "https://mysign.test.local",
            ClientId = "cid",
            ClientSecret = "secret",
            ProfileId = "pid",
        };
        var monitor = Substitute.For<IOptionsMonitor<ViettelMysignOptions>>();
        monitor.CurrentValue.Returns(opts);
        return monitor;
    }

    private static ViettelMysignLoginClient BuildClient(IMemoryCache cache, StubHandler handler)
    {
        var http = new HttpClient(handler);
        return new ViettelMysignLoginClient(
            http, StubOptions(), cache, NullLogger<ViettelMysignLoginClient>.Instance);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesUnderSizeLimitedCache_DoesNotThrow()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var handler = new StubHandler("""{"access_token":"tok-123","expires_in":3600}""");
        var client = BuildClient(cache, handler);

        var token = await client.GetAccessTokenAsync("017091004878", CancellationToken.None);

        token.Should().Be("tok-123");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAccessTokenAsync_SecondCall_ServedFromCache()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var handler = new StubHandler("""{"access_token":"tok-123","expires_in":3600}""");
        var client = BuildClient(cache, handler);

        await client.GetAccessTokenAsync("017091004878", CancellationToken.None);
        var second = await client.GetAccessTokenAsync("017091004878", CancellationToken.None);

        second.Should().Be("tok-123");
        handler.CallCount.Should().Be(1, because: "the second call must be served from the cached token");
    }
}
