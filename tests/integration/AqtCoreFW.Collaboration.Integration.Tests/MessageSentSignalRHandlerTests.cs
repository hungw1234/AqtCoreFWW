using System.Diagnostics.Metrics;
using AqtCoreFW.Collaboration.Application.Telemetry;
using AqtCoreFW.Collaboration.Domain.Events;
using AqtCoreFW.Collaboration.Infrastructure.Realtime;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AqtCoreFW.Collaboration.Integration.Tests;

/// <summary>
/// Pure unit tests for MessageSentSignalRHandler broadcast resilience — no SQL/host fixture, so
/// deliberately NOT in the SqlServerCollection. The handler runs synchronously on MessageSentEvent
/// publish, which fires AFTER the message is persisted; a read-back/broadcast fault must be
/// swallowed so a committed write never surfaces to the client as a 500.
/// </summary>
public sealed class MessageSentSignalRHandlerTests
{
    private static MessageSentEvent SampleEvent() =>
        new(Guid.NewGuid(), 42L, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static MessageSentSignalRHandler Build(
        IReadDbConnection db, ILogger<MessageSentSignalRHandler> logger) =>
        new(db,
            Substitute.For<IIdentityModuleClient>(),
            Substitute.For<IHubContext<ChatHub>>(),
            logger);

    [Fact]
    public async Task Handle_WhenReadOrBroadcastFaults_SwallowsExceptionAndLogs()
    {
        var db = Substitute.For<IReadDbConnection>();
        db.QueryFirstOrDefaultAsync<MessageSentSignalRHandler.MessageRow>(
              Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
          .ThrowsAsync(new InvalidOperationException("simulated read/broadcast fault"));
        var logger = Substitute.For<ILogger<MessageSentSignalRHandler>>();

        var act = async () => await Build(db, logger).Handle(SampleEvent(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.ReceivedCalls().Should().NotBeEmpty(); // log-and-swallow recorded the fault
    }

    [Fact]
    public async Task Handle_WhenBroadcastFaults_IncrementsFailureCounter()
    {
        // The swallowed fault must also bump the observability counter so a sustained spike is
        // visible on the dashboard — the log alone gives no aggregate signal.
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

        var db = Substitute.For<IReadDbConnection>();
        db.QueryFirstOrDefaultAsync<MessageSentSignalRHandler.MessageRow>(
              Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
          .ThrowsAsync(new InvalidOperationException("simulated read/broadcast fault"));
        var logger = Substitute.For<ILogger<MessageSentSignalRHandler>>();

        await Build(db, logger).Handle(SampleEvent(), CancellationToken.None);
        listener.Dispose(); // flush pending measurements

        recorded.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenCancelled_PropagatesOperationCanceled()
    {
        // The catch filter excludes OperationCanceledException so cooperative cancellation
        // (request aborted) still propagates — it is not a swallowable broadcast fault.
        var db = Substitute.For<IReadDbConnection>();
        db.QueryFirstOrDefaultAsync<MessageSentSignalRHandler.MessageRow>(
              Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
          .ThrowsAsync(new OperationCanceledException());
        var logger = Substitute.For<ILogger<MessageSentSignalRHandler>>();

        var act = async () => await Build(db, logger).Handle(SampleEvent(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Handle_WhenMessageMissing_ReturnsWithoutThrow()
    {
        // Unconfigured query → null row → early return; no broadcast, no fault.
        var db = Substitute.For<IReadDbConnection>();
        var logger = Substitute.For<ILogger<MessageSentSignalRHandler>>();

        var act = async () => await Build(db, logger).Handle(SampleEvent(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
