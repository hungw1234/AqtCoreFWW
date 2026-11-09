using AqtCoreFW.Infrastructure.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Cors;

/// <summary>
/// Regression-lock for the public API CORS allow-list. A custom request header that the SPA sends but
/// the policy omits is silently fatal: the browser blocks it at preflight, while server-side tests
/// (WebApplicationFactory / curl) bypass preflight and stay green. This pins the BUILT policy against
/// an independent expected set, so dropping (or adding) a header fails the build and forces a review.
///
/// The expected lists are intentionally hard-coded here — NOT imported from the production arrays —
/// otherwise the assertion would be tautological and catch nothing.
/// </summary>
public sealed class GovApiCorsPolicyTests
{
    private static CorsPolicy BuildPolicy()
    {
        var services = new ServiceCollection();
        services.AddGovApiCors(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IOptions<CorsOptions>>()
            .Value.GetPolicy(GovApiCorsExtensions.PolicyName);
        policy.Should().NotBeNull("the GovApi CORS policy must be registered");
        return policy!;
    }

    [Fact]
    public void Policy_AllowsExactlyTheExpectedRequestHeaders()
    {
        string[] expected =
        [
            "Content-Type", "Authorization", "X-Api-Key", "X-XSRF-TOKEN",
            "X-Correlation-Id", "X-Tenant-Id", "X-Request-Token",
            "Accept", "Accept-Language", "If-Match"
        ];

        BuildPolicy().Headers.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Policy_AllowsXRequestTokenHeader()
    {
        // The header the anti-double-submit middleware reads and the BP-run start POST sends.
        // Its omission previously broke the browser flow while every server test passed.
        BuildPolicy().Headers.Should().Contain("X-Request-Token");
    }

    [Fact]
    public void Policy_ExposesExpectedResponseHeaders()
    {
        string[] expectedExposed =
        [
            "X-Mfa-Required", "X-Must-Change-Password", "X-Password-Expired", "X-Correlation-Id"
        ];

        BuildPolicy().ExposedHeaders.Should().BeEquivalentTo(expectedExposed);
    }

    [Fact]
    public void Policy_AllowsExpectedMethodsAndCredentials()
    {
        var policy = BuildPolicy();
        policy.Methods.Should().BeEquivalentTo("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS");
        policy.SupportsCredentials.Should().BeTrue();
    }
}
