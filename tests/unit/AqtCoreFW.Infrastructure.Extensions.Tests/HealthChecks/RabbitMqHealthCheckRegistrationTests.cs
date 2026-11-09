using AqtCoreFW.Infrastructure.HealthChecks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.HealthChecks;

/// <summary>
/// Unit tests for the RabbitMQ readiness probe registered by <see cref="HealthCheckConfiguration.AddGovHealthChecks"/>.
///
/// The bus defaults to the InMemory transport (MessagingRegistration), so the probe is gated: an
/// unconditional broker probe would report Degraded about a broker the system never connects to.
///
/// Truth table:
///   Bus:Transport = "RabbitMQ"  → "rabbitmq" registered, Degraded, tagged ready
///   Bus:Transport = "InMemory"  → no "rabbitmq" registration
///   Bus:Transport absent        → no "rabbitmq" registration (defaults to InMemory)
///
/// These drive the real AddGovHealthChecks registration path rather than a hand-rolled equivalent,
/// so a change to the gate is caught here instead of in production.
/// </summary>
public sealed class RabbitMqHealthCheckRegistrationTests
{
    private const string RabbitMqProbeName = "rabbitmq";

    /// <summary>Builds the real registration and returns what /health/ready would expose.</summary>
    private static ICollection<HealthCheckRegistration> Register(
        params (string Key, string Value)[] pairs)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p =>
                new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        var services = new ServiceCollection();
        services.AddSingleton(env);
        services.AddSingleton<IConfiguration>(config);

        services.AddGovHealthChecks(config);

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
    }

    /// <summary>Connection strings the core probes need; irrelevant to the transport gate itself.</summary>
    private static (string, string)[] CoreConfig() =>
    [
        ("ConnectionStrings:Default", "Server=.;Database=test;Trusted_Connection=True;"),
        ("Redis:ConnectionString", "localhost:6379")
    ];

    [Fact]
    public void Transport_RabbitMq_RegistersRabbitMqProbe()
    {
        var registrations = Register([.. CoreConfig(), ("Bus:Transport", "RabbitMQ")]);

        var probe = registrations.SingleOrDefault(r => r.Name == RabbitMqProbeName);

        probe.Should().NotBeNull("RabbitMQ is the configured transport, so readiness must cover it");
        probe!.FailureStatus.Should().Be(HealthStatus.Degraded,
            "the outbox absorbs a broker outage — it must not pull the pod out of rotation");
        probe.Tags.Should().Contain("ready", "the probe must surface on /health/ready");
    }

    [Fact]
    public void Transport_InMemory_DoesNotRegisterRabbitMqProbe()
    {
        var registrations = Register([.. CoreConfig(), ("Bus:Transport", "InMemory")]);

        registrations.Should().NotContain(r => r.Name == RabbitMqProbeName,
            "probing a broker the InMemory transport never connects to would report a false outage");
    }

    [Fact]
    public void Transport_Absent_DoesNotRegisterRabbitMqProbe()
    {
        var registrations = Register(CoreConfig());

        registrations.Should().NotContain(r => r.Name == RabbitMqProbeName,
            "an absent Bus:Transport means the InMemory default, same as setting it explicitly");
    }

    [Theory]
    [InlineData("RabbitMQ")]
    [InlineData("InMemory")]
    public void CoreProbes_RegisteredRegardlessOfTransport(string transport)
    {
        var registrations = Register([.. CoreConfig(), ("Bus:Transport", transport)]);

        registrations.Select(r => r.Name).Should()
            .Contain(["sqlserver", "redis", "audit-read-isolation"],
                "the transport gate must not disturb the probes that are always registered");
    }
}
