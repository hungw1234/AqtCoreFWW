using System.Diagnostics.Metrics;
using AqtCoreFW.Collaboration.Application.Telemetry;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.Collaboration.Infrastructure.Realtime;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AqtCoreFW.Collaboration.Integration.Tests;

/// <summary>
/// Pure unit tests for MessageReadSignalRHandler broadcast resilience — no SQL/host fixture. The
/// handler runs on MessageReadEvent publish, AFTER the receipt is persisted; a name-resolve or
/// broadcast fault must be swallowed so a committed write never surfaces to the client as a 500.
/// </summary>
public sealed class MessageReadSignalRHandlerTests
{
    private static MessageReadEvent SampleEvent() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());

    private static MessageReadSignalRHandler Build(
        IIdentityModuleClient identity, IHubContext<ChatHub> hub, ILogger<MessageReadSignalRHandler> logger) =>
        new(identity, hub, logger);

    [Fact]
    public async Task Handle_WhenNameResolveOrBroadcastFaults_SwallowsExceptionAndLogs()
    {
        var identity = Substitute.For<IIdentityModuleClient>();
        identity.GetUserInfoByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("simulated resolve/broadcast fault"));
        var logger = Substitute.For<ILogger<MessageReadSignalRHandler>>();

        var act = async () =>
            await Build(identity, Substitute.For<IHubContext<ChatHub>>(), logger).Handle(SampleEvent(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenBroadcastFaults_IncrementsFailureCounter()
    {
        long recorded = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == CollaborationMetrics.MeterName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, _, _) =>
            {
                if (instrument.Name == "collab.signalr_broadcast.failures")
                    Interlocked.Add(ref recorded, measurement);
            });
        listener.Start();

        var identity = Substitute.For<IIdentityModuleClient>();
        identity.GetUserInfoByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("simulated fault"));
        var logger = Substitute.For<ILogger<MessageReadSignalRHandler>>();

        await Build(identity, Substitute.For<IHubContext<ChatHub>>(), logger).Handle(SampleEvent(), CancellationToken.None);
        listener.Dispose();

        recorded.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenCancelled_PropagatesOperationCanceled()
    {
        var identity = Substitute.For<IIdentityModuleClient>();
        identity.GetUserInfoByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var logger = Substitute.For<ILogger<MessageReadSignalRHandler>>();

        var act = async () =>
            await Build(identity, Substitute.For<IHubContext<ChatHub>>(), logger).Handle(SampleEvent(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
