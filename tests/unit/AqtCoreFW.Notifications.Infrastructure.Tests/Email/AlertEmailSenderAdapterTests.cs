using AqtCoreFW.Notifications.Application.Providers;
using AqtCoreFW.Notifications.Infrastructure;
using AqtCoreFW.Notifications.Infrastructure.Email;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AqtCoreFW.Notifications.Infrastructure.Tests.Email;

/// <summary>
/// <see cref="AlertEmailSenderAdapter"/>: the seam shared-infrastructure alerting resolves to reach
/// SMTP. The registration test drives the REAL AddNotificationsInfrastructure() — the consumer
/// (AlertEvaluationJob) lives in a different assembly, so a missing registration here would surface
/// only at runtime, never at compile time.
/// </summary>
public sealed class AlertEmailSenderAdapterTests
{
    [Fact]
    public async Task SendAsync_PassesAddressSubjectAndBodyThrough_Unmodified()
    {
        var inner = Substitute.For<IEmailSender>();
        var sut = new AlertEmailSenderAdapter(inner);
        using var cts = new CancellationTokenSource();

        await sut.SendAsync("ops@example.gov.vn", "[ALERT] disk", "body text", cts.Token);

        await inner.Received(1).SendAsync("ops@example.gov.vn", "[ALERT] disk", "body text", cts.Token);
    }

    [Fact]
    public async Task SendAsync_PropagatesInnerFailure()
    {
        var inner = Substitute.For<IEmailSender>();
        inner.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("smtp down"));

        var act = async () => await new AlertEmailSenderAdapter(inner)
            .SendAsync("ops@example.gov.vn", "s", "b");

        // AlertEvaluationJob relies on the throw to log Critical rather than drop the alert silently.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void AddNotificationsInfrastructure_RegistersIAlertEmailSender()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=test;Trusted_Connection=True;",
                ["Email:Host"] = "localhost",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationsInfrastructure(configuration);

        // Resolve the concrete descriptor rather than building the whole graph — the DbContext and
        // SignalR registrations in this method need more of the host than a unit test provides.
        var descriptor = services.Single(d => d.ServiceType == typeof(IAlertEmailSender));

        descriptor.ImplementationType.Should().Be<AlertEmailSenderAdapter>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }
}
