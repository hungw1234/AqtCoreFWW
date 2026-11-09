using AqtCoreFW.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="NonAuditIsolationHealthCheck"/> — the readiness signal that surfaces a
/// dormant audit read-isolation boundary (NonAudit login unprovisioned) as Degraded, never Unhealthy.
///
/// Truth table:
///   NonAudit set (any env)        → Healthy   ("isolation active")
///   NonAudit unset, non-Production → Healthy   ("opt-in fallback expected outside prod")
///   NonAudit unset, Production     → Degraded  ("dormant boundary — visible, not silent, not fatal")
/// </summary>
public sealed class NonAuditIsolationHealthCheckTests
{
    private static readonly HealthCheckContext Context = new()
    {
        Registration = new HealthCheckRegistration("audit-read-isolation", _ => null!, null, null)
    };

    private static IConfiguration Config(params (string Key, string Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static IWebHostEnvironment FakeEnv(string envName)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(envName);
        return env;
    }

    private static NonAuditIsolationHealthCheck BuildCheck(IConfiguration config, string envName) =>
        new(config, FakeEnv(envName));

    [Fact]
    public async Task Production_NonAuditUnset_ReturnsDegraded()
    {
        var config = Config(("ConnectionStrings:Default", "Server=.;Database=broad;"));

        var result = await BuildCheck(config, "Production").CheckHealthAsync(Context);

        result.Status.Should().Be(HealthStatus.Degraded,
            "an unprovisioned NonAudit login in Production leaves reads on the broad Default login — visible, not silent");
        result.Description.Should().Contain("dormant");
    }

    [Fact]
    public async Task Production_NonAuditSet_ReturnsHealthy()
    {
        var config = Config(
            ("ConnectionStrings:Default", "Server=.;Database=broad;"),
            ("ConnectionStrings:NonAudit", "Server=.;Database=scoped;User Id=nonaudit;"));

        var result = await BuildCheck(config, "Production").CheckHealthAsync(Context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Development_NonAuditUnset_ReturnsHealthy()
    {
        var config = Config(("ConnectionStrings:Default", "Server=.;Database=broad;"));

        var result = await BuildCheck(config, "Development").CheckHealthAsync(Context);

        result.Status.Should().Be(HealthStatus.Healthy,
            "outside Production the opt-in fallback is expected — no readiness noise");
    }
}
