using AqtCoreFW.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Security.Tests.Injection;

/// <summary>
/// SSRF prevention tests for WebhookUrlValidator — TC-SEC-INJ-006, TC-SEC-INJ-007.
/// Validates HTTPS enforcement and private/reserved IP blocking.
/// </summary>
public sealed class SsrfPreventionTests
{
    private readonly WebhookUrlValidator _validator = new(NullLogger<WebhookUrlValidator>.Instance);

    // --- HTTPS Enforcement ---

    [Fact]
    [Trait("Category", "Security")]
    public async Task HttpUrl_Rejected()
    {
        var result = await _validator.ValidateAsync("http://example.com/webhook");

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("HTTPS");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task InvalidUrl_Rejected()
    {
        var result = await _validator.ValidateAsync("not-a-url");

        result.IsFailed.Should().BeTrue();
    }

    // --- Private/Reserved IPv4 Blocking ---

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("https://10.0.0.1/webhook")]
    [InlineData("https://172.16.0.1/webhook")]
    [InlineData("https://192.168.1.1/webhook")]
    [InlineData("https://127.0.0.1/webhook")]
    [InlineData("https://169.254.1.1/webhook")]
    [InlineData("https://100.64.0.1/webhook")]
    public async Task PrivateIpLiteral_Rejected(string url)
    {
        var result = await _validator.ValidateAsync(url);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("private/reserved");
    }

    // --- IPv6 Blocking ---

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("https://[::1]/webhook")]       // Loopback
    [InlineData("https://[fe80::1]/webhook")]    // Link-local
    [InlineData("https://[fd00::1]/webhook")]    // ULA
    public async Task PrivateIpv6Literal_Rejected(string url)
    {
        var result = await _validator.ValidateAsync(url);

        result.IsFailed.Should().BeTrue();
    }

    // --- Cloud Metadata ---

    [Fact]
    [Trait("Category", "Security")]
    public async Task CloudMetadataIp_Rejected()
    {
        // AWS/GCP metadata endpoint
        var result = await _validator.ValidateAsync("https://169.254.169.254/latest/meta-data/");

        result.IsFailed.Should().BeTrue();
    }

    // --- IPv4-mapped IPv6 bypass attempt ---

    [Fact]
    [Trait("Category", "Security")]
    public async Task Ipv4MappedIpv6_LoopbackBypass_Rejected()
    {
        // ::ffff:127.0.0.1 is IPv4-mapped IPv6 for loopback — must be caught
        var result = await _validator.ValidateAsync("https://[::ffff:127.0.0.1]/webhook");

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Ipv4MappedIpv6_PrivateBypass_Rejected()
    {
        // ::ffff:10.0.0.1 is IPv4-mapped IPv6 for RFC1918 — must be caught
        var result = await _validator.ValidateAsync("https://[::ffff:10.0.0.1]/webhook");

        result.IsFailed.Should().BeTrue();
    }

    // --- Broadcast ---

    [Fact]
    [Trait("Category", "Security")]
    public async Task BroadcastAddress_Rejected()
    {
        var result = await _validator.ValidateAsync("https://255.255.255.255/webhook");

        result.IsFailed.Should().BeTrue();
    }
}
