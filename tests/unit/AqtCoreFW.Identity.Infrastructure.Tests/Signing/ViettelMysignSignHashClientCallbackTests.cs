using System.Net;
using System.Text;
using System.Text.Json;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// False-green guard tests for async=3 (callback) mode in ViettelMysignSignHashClient.
/// Authoritative (client-level) tests that capture the outgoing HTTP request body and verify
/// the callback URL is serialized correctly, or absent (WhenWritingNull behavior for async 0/2).
///
/// These tests pin the exact wire shape — a CONTRACT between the client and Mysign.
/// </summary>
public sealed class ViettelMysignSignHashClientCallbackTests
{
    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }
        public int CallCount { get; private set; }
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public CapturingHttpMessageHandler(
            string responseJson = """{"transactionId":"tx-1"}""",
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static IOptionsMonitor<ViettelMysignOptions> StubOptionsWithCallback(string callbackUrl)
    {
        var opts = new ViettelMysignOptions
        {
            BaseUrl = "https://mysign.test.local",
            ClientId = "cid",
            ClientSecret = "secret",
            ProfileId = "pid",
            CallbackUrl = callbackUrl,
        };
        var monitor = Substitute.For<IOptionsMonitor<ViettelMysignOptions>>();
        monitor.CurrentValue.Returns(opts);
        return monitor;
    }

    private static IOptionsMonitor<ViettelMysignOptions> StubOptionsWithoutCallback()
    {
        var opts = new ViettelMysignOptions
        {
            BaseUrl = "https://mysign.test.local",
            ClientId = "cid",
            ClientSecret = "secret",
            ProfileId = "pid",
            CallbackUrl = null,
        };
        var monitor = Substitute.For<IOptionsMonitor<ViettelMysignOptions>>();
        monitor.CurrentValue.Returns(opts);
        return monitor;
    }

    private static ViettelMysignSignHashClient BuildClient(
        CapturingHttpMessageHandler handler,
        IOptionsMonitor<ViettelMysignOptions> optionsMonitor)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://mysign.test.local"),
        };
        return new ViettelMysignSignHashClient(
            http, optionsMonitor, NullLogger<ViettelMysignSignHashClient>.Instance);
    }

    [Fact]
    public async Task SignHashRawAsync_AsyncMode3_WithCallbackUrl_IncludesCallbackUrlInRequestBody()
    {
        const string callbackUrl = "https://callback.example.test/api/signing/mysign-callback/result";
        var handler = new CapturingHttpMessageHandler();
        var optionsMonitor = StubOptionsWithCallback(callbackUrl);
        var client = BuildClient(handler, optionsMonitor);

        var hashBytes = new byte[32]; // dummy 32-byte SHA-256 hash
        var response = await client.SignHashRawAsync(
            userId: "017091004878",
            credentialId: "017091004878_9710405_20260603200344",
            hashBytes: hashBytes,
            accessToken: "tok-123",
            asyncMode: 3, // async=3 callback mode
            documentName: "TestDoc",
            ct: default);

        // Verify the stub was called exactly once.
        handler.CallCount.Should().Be(1);

        // Verify the request body contains the callbackUrl field with the exact URL.
        handler.CapturedRequestBody.Should().NotBeNullOrEmpty();
        var requestJson = JsonSerializer.Deserialize<JsonElement>(handler.CapturedRequestBody);

        // Pin the field name — [UNVERIFIED] in code but test pins whatever is serialized.
        requestJson.TryGetProperty("callbackUrl", out var callbackUrlElement).Should().BeTrue(
            because: "async=3 request must include callbackUrl field");
        callbackUrlElement.GetString().Should().Be(callbackUrl,
            because: "callbackUrl must match the configured value exactly");

        // Sanity check: async field should be 3.
        requestJson.TryGetProperty("async", out var asyncElement).Should().BeTrue();
        asyncElement.GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task SignHashRawAsync_AsyncMode0_WithCallbackUrlNull_OmitsCallbackUrlFromRequestBody()
    {
        var handler = new CapturingHttpMessageHandler();
        var optionsMonitor = StubOptionsWithCallback("https://callback.example.test/api/signing/mysign-callback/result");
        var client = BuildClient(handler, optionsMonitor);

        var hashBytes = new byte[32];
        await client.SignHashRawAsync(
            userId: "017091004878",
            credentialId: "017091004878_9710405_20260603200344",
            hashBytes: hashBytes,
            accessToken: "tok-123",
            asyncMode: 0, // async=0 sync mode
            documentName: "TestDoc",
            ct: default);

        handler.CallCount.Should().Be(1);
        handler.CapturedRequestBody.Should().NotBeNullOrEmpty();
        var requestJson = JsonSerializer.Deserialize<JsonElement>(handler.CapturedRequestBody);

        // For async=0, callbackUrl should NOT be present (WhenWritingNull omits null fields).
        requestJson.TryGetProperty("callbackUrl", out _).Should().BeFalse(
            because: "async=0 (sync mode) must NOT emit callbackUrl field when null (WhenWritingNull policy)");

        // Sanity check: async field should be 0.
        requestJson.TryGetProperty("async", out var asyncElement).Should().BeTrue();
        asyncElement.GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task SignHashRawAsync_AsyncMode2_WithCallbackUrlNull_OmitsCallbackUrlFromRequestBody()
    {
        var handler = new CapturingHttpMessageHandler();
        var optionsMonitor = StubOptionsWithCallback("https://callback.example.test/api/signing/mysign-callback/result");
        var client = BuildClient(handler, optionsMonitor);

        var hashBytes = new byte[32];
        await client.SignHashRawAsync(
            userId: "017091004878",
            credentialId: "017091004878_9710405_20260603200344",
            hashBytes: hashBytes,
            accessToken: "tok-123",
            asyncMode: 2, // async=2 async-poll deferred mode
            documentName: "TestDoc",
            ct: default);

        handler.CallCount.Should().Be(1);
        handler.CapturedRequestBody.Should().NotBeNullOrEmpty();
        var requestJson = JsonSerializer.Deserialize<JsonElement>(handler.CapturedRequestBody);

        // For async=2, callbackUrl should NOT be present (WhenWritingNull omits null fields).
        requestJson.TryGetProperty("callbackUrl", out _).Should().BeFalse(
            because: "async=2 (async-poll mode) must NOT emit callbackUrl field when null (WhenWritingNull policy)");

        // Sanity check: async field should be 2.
        requestJson.TryGetProperty("async", out var asyncElement).Should().BeTrue();
        asyncElement.GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task SignHashRawAsync_AsyncMode3_WithNullCallbackUrl_ThrowsBeforeHttpCall()
    {
        var handler = new CapturingHttpMessageHandler();
        var optionsMonitor = StubOptionsWithoutCallback(); // CallbackUrl = null
        var client = BuildClient(handler, optionsMonitor);

        var hashBytes = new byte[32];
        var act = () => client.SignHashRawAsync(
            userId: "017091004878",
            credentialId: "017091004878_9710405_20260603200344",
            hashBytes: hashBytes,
            accessToken: "tok-123",
            asyncMode: 3, // async=3 callback mode
            documentName: "TestDoc",
            ct: default);

        await act.Should().ThrowAsync<MysignSignHashException>()
            .WithMessage("*async-callback*async=3*requires*CallbackUrl*");

        // Fail-loud before HTTP: the stub handler must NEVER be called.
        handler.CallCount.Should().Be(0,
            because: "async=3 with null CallbackUrl must throw before sending HTTP request");
    }

    [Fact]
    public async Task SignHashRawAsync_AsyncMode3_WithWhitespaceCallbackUrl_ThrowsBeforeHttpCall()
    {
        var handler = new CapturingHttpMessageHandler();
        var optionsMonitor = StubOptionsWithCallback("   "); // whitespace-only URL
        var client = BuildClient(handler, optionsMonitor);

        var hashBytes = new byte[32];
        var act = () => client.SignHashRawAsync(
            userId: "017091004878",
            credentialId: "017091004878_9710405_20260603200344",
            hashBytes: hashBytes,
            accessToken: "tok-123",
            asyncMode: 3,
            documentName: "TestDoc",
            ct: default);

        await act.Should().ThrowAsync<MysignSignHashException>()
            .WithMessage("*async-callback*async=3*requires*CallbackUrl*");

        // Fail-loud before HTTP: the stub handler must NEVER be called.
        handler.CallCount.Should().Be(0,
            because: "async=3 with whitespace-only CallbackUrl must throw before sending HTTP request");
    }

    [Fact]
    public async Task SignHashRawAsync_AsyncMode0_WithNullCallbackUrl_SucceedsWithoutCallbackUrl()
    {
        var handler = new CapturingHttpMessageHandler();
        var optionsMonitor = StubOptionsWithoutCallback(); // CallbackUrl = null
        var client = BuildClient(handler, optionsMonitor);

        var hashBytes = new byte[32];
        var response = await client.SignHashRawAsync(
            userId: "017091004878",
            credentialId: "017091004878_9710405_20260603200344",
            hashBytes: hashBytes,
            accessToken: "tok-123",
            asyncMode: 0, // async=0 sync mode — no callback needed
            documentName: "TestDoc",
            ct: default);

        // Should succeed — no CallbackUrl required for async=0.
        response.Should().NotBeNull();
        handler.CallCount.Should().Be(1, because: "HTTP call should proceed for async=0 regardless of CallbackUrl");
    }

    [Fact]
    public async Task SignHashRawAsync_AsyncMode3_CallbackUrlCorrect_UsesConfiguredValue()
    {
        // Test that the exact configured CallbackUrl is used, not computed or transformed.
        const string uniqueCallbackUrl = "https://unique-callback-id-12345.example.test/webhook";
        var handler = new CapturingHttpMessageHandler();
        var optionsMonitor = StubOptionsWithCallback(uniqueCallbackUrl);
        var client = BuildClient(handler, optionsMonitor);

        var hashBytes = new byte[32];
        await client.SignHashRawAsync(
            userId: "017091004878",
            credentialId: "017091004878_9710405_20260603200344",
            hashBytes: hashBytes,
            accessToken: "tok-123",
            asyncMode: 3,
            documentName: "TestDoc",
            ct: default);

        handler.CapturedRequestBody.Should().NotBeNullOrEmpty();
        var requestJson = JsonSerializer.Deserialize<JsonElement>(handler.CapturedRequestBody);
        requestJson.TryGetProperty("callbackUrl", out var element).Should().BeTrue();
        element.GetString().Should().Be(uniqueCallbackUrl,
            because: "the exact configured CallbackUrl must be sent unmodified");
    }
}
