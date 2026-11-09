using AqtCoreFW.Identity.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests;

/// <summary>
/// Tests for <see cref="IdentityInfrastructureRegistration.IsVneIdStubAllowed"/> — the gate that
/// decides whether the VNeID stub connector may be registered. The false branch makes
/// AddIdentityInfrastructure throw (fail-fast: no real VNeIdHttpConnector exists yet), so BOTH
/// branches are asserted here: the rehearsal escape hatch must open ONLY on an explicit
/// VneId:AllowStub=true, never by default in Production.
/// </summary>
public sealed class VneIdStubGateTests
{
    private static IWebHostEnvironment Env(string name)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    // ── Fail-fast branch (the prod-safety default) ─────────────────────────

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDevEnvironment_WithoutAllowStub_IsNotAllowed(string environment)
    {
        IdentityInfrastructureRegistration
            .IsVneIdStubAllowed(Env(environment), Config())
            .Should().BeFalse("a real deploy without a real connector must refuse to boot");
    }

    [Fact]
    public void Production_WithAllowStubFalse_IsNotAllowed()
    {
        IdentityInfrastructureRegistration
            .IsVneIdStubAllowed(Env("Production"), Config(("VneId:AllowStub", "false")))
            .Should().BeFalse();
    }

    // ── Rehearsal escape hatch ─────────────────────────────────────────────

    [Fact]
    public void Production_WithAllowStubTrue_IsAllowed()
    {
        IdentityInfrastructureRegistration
            .IsVneIdStubAllowed(Env("Production"), Config(("VneId:AllowStub", "true")))
            .Should().BeTrue("rehearsal clusters opt in explicitly via values-local");
    }

    // ── Dev-family environments stay allowed regardless of the flag ────────

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Benchmark")]
    public void DevFamilyEnvironment_IsAllowed_WithoutFlag(string environment)
    {
        IdentityInfrastructureRegistration
            .IsVneIdStubAllowed(Env(environment), Config())
            .Should().BeTrue();
    }
}
