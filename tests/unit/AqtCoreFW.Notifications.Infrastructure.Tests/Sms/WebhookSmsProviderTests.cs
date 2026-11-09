using System.Net;
using System.Text.Json;
using AqtCoreFW.Notifications.Infrastructure.Sms;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AqtCoreFW.Notifications.Infrastructure.Tests.Sms;

/// <summary>
/// TC-NOT-I004: WebhookSmsProvider constructs correct HTTP request (JSON body, correct URL).
/// Uses a custom HttpMessageHandler substitute to inspect outgoing requests without real HTTP calls.
/// </summary>
public sealed class WebhookSmsProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task SendAsync_CorrectJsonPayload_PostedToWebhookUrl()
    {
        // TC-NOT-I004: provider POSTs JSON {to, text} to configured WebhookUrl
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("SmsWebhook").Returns(httpClient);

        var opts = Options.Create(new SmsWebhookOptions
        {
            WebhookUrl = "http://sms-gateway.local/send",
            BearerToken = "test-token"
        });

        var sut = new WebhookSmsProvider(factory, opts, NullLogger<WebhookSmsProvider>.Instance);

        await sut.SendAsync("+84901234567", "Mã OTP: 123456");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be("http://sms-gateway.local/send");

        handler.LastBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(handler.LastBody!);
        doc.RootElement.GetProperty("to").GetString().Should().Be("+84901234567");
        doc.RootElement.GetProperty("text").GetString().Should().Be("Mã OTP: 123456");
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        // Provider calls EnsureSuccessStatusCode — non-200 responses propagate as exception
        var handler = new CapturingHandler();
        // Override: return 500
        var failingHandler = new FailingHandler(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(failingHandler);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("SmsWebhook").Returns(httpClient);

        var opts = Options.Create(new SmsWebhookOptions
        {
            WebhookUrl = "http://sms-gateway.local/send",
            BearerToken = "test-token"
        });

        var sut = new WebhookSmsProvider(factory, opts, NullLogger<WebhookSmsProvider>.Instance);

        var act = async () => await sut.SendAsync("+84901234567", "Test");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class FailingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
