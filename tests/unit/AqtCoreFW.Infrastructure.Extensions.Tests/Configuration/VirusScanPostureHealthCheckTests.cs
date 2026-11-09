using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="VirusScanPostureHealthCheck"/> — the readiness signal that surfaces uploads
/// being stored without a virus scan as Degraded, never Unhealthy.
///
/// Truth table:
///   flag on (any env)              → Healthy   ("scanning active")
///   flag off, non-Production       → Healthy   ("no local clamd expected — no readiness noise")
///   flag off, Production           → Degraded  ("visible, not silent, not fatal")
///   flag service missing (any env) → Degraded  ("posture unknown — must not throw on a probe")
/// </summary>
public sealed class VirusScanPostureHealthCheckTests
{
    private const string VirusScanFlag = "feature:files.virus-scan";

    private static readonly HealthCheckContext Context = new()
    {
        Registration = new HealthCheckRegistration("upload-virus-scan", _ => null!, null, null)
    };

    private static IWebHostEnvironment FakeEnv(string envName)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(envName);
        return env;
    }

    private static IFeatureFlagService FakeFlags(bool enabled)
    {
        var flags = Substitute.For<IFeatureFlagService>();
        flags.IsEnabled(VirusScanFlag, Arg.Any<string?>()).Returns(enabled);
        return flags;
    }

    [Fact]
    public async Task Production_ScanDisabled_ReturnsDegraded()
    {
        var check = new VirusScanPostureHealthCheck(FakeEnv("Production"), FakeFlags(enabled: false));

        var result = await check.CheckHealthAsync(Context);

        result.Status.Should().Be(HealthStatus.Degraded,
            "uploads stored without scanning in Production must be visible on /health/ready, but scanning-off is the shipped state until clamd exists — degrading the pod out of rotation would cause an outage over a known posture");
        result.Description.Should().Contain("without scanning");
    }

    [Fact]
    public async Task Production_ScanEnabled_ReturnsHealthy()
    {
        var check = new VirusScanPostureHealthCheck(FakeEnv("Production"), FakeFlags(enabled: true));

        var result = await check.CheckHealthAsync(Context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Development_ScanDisabled_ReturnsHealthy()
    {
        var check = new VirusScanPostureHealthCheck(FakeEnv("Development"), FakeFlags(enabled: false));

        var result = await check.CheckHealthAsync(Context);

        result.Status.Should().Be(HealthStatus.Healthy,
            "outside Production there is normally no clamd to scan against — off is expected, so readiness stays clean");
    }

    [Fact]
    public async Task FeatureFlagsUnavailable_ReturnsDegraded_NotThrow()
    {
        // A host can register the shared health checks without the SystemParams module that provides
        // IFeatureFlagService. The probe must report "unknown" rather than throwing, which would surface
        // as an opaque 500 on /health/ready instead of a diagnosable check result.
        var check = new VirusScanPostureHealthCheck(FakeEnv("Production"), featureFlags: null);

        var result = await check.CheckHealthAsync(Context);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("unknown");
    }

    [Fact]
    public async Task ReadsTheGlobalRow_NotATenantScopedOne()
    {
        // This probe must read the GLOBAL posture. The enforcement gates deliberately read
        // tenant-with-fallback (a tenant override is theirs to honour), but a deployment-level readiness
        // check has no tenant, and passing one would make the answer depend on whichever tenant it picked.
        // Asserting the null argument is what makes that distinction real rather than incidental.
        var flags = Substitute.For<IFeatureFlagService>();
        flags.IsEnabled(Arg.Any<string>(), Arg.Any<string?>()).Returns(false);

        await new VirusScanPostureHealthCheck(FakeEnv("Production"), flags).CheckHealthAsync(Context);

        flags.Received(1).IsEnabled(VirusScanFlag, null);
    }
}
