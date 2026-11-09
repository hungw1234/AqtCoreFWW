using AqtCoreFW.Infrastructure.HealthChecks;
using FluentAssertions;
using RabbitMQ.Client;
using NSubstitute;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.HealthChecks;

/// <summary>
/// Unit tests for <see cref="ProbeConnectionSource"/> — the connection cache behind the RabbitMQ
/// readiness probe.
///
/// The package's async AddRabbitMQ overload calls its factory on every probe and never disposes the
/// result, so an uncached factory leaks one broker connection per readiness poll (~8.6k/pod/day at a
/// 10s cadence) until the broker exhausts its file descriptors. These tests pin the caching that
/// prevents it:
///
///   connection open        → reuse, do not reconnect
///   connection dropped     → dispose the dead one, reconnect once
///   concurrent probes      → a single connect, not one per caller
/// </summary>
public sealed class ProbeConnectionSourceTests
{
    private static IConnection OpenConnection()
    {
        var conn = Substitute.For<IConnection>();
        conn.IsOpen.Returns(true);
        return conn;
    }

    private static IConnection ClosedConnection()
    {
        var conn = Substitute.For<IConnection>();
        conn.IsOpen.Returns(false);
        return conn;
    }

    [Fact]
    public async Task FirstProbe_OpensAConnection()
    {
        var connection = OpenConnection();
        var connects = 0;
        var source = new ProbeConnectionSource(() => { connects++; return Task.FromResult(connection); });

        var result = await source.GetAsync();

        result.Should().BeSameAs(connection);
        connects.Should().Be(1);
    }

    [Fact]
    public async Task RepeatProbes_ReuseTheOpenConnection()
    {
        var connection = OpenConnection();
        var connects = 0;
        var source = new ProbeConnectionSource(() => { connects++; return Task.FromResult(connection); });

        for (var i = 0; i < 5; i++)
            (await source.GetAsync()).Should().BeSameAs(connection);

        connects.Should().Be(1,
            "an uncached factory leaks one connection per readiness poll until the broker runs out of descriptors");
    }

    [Fact]
    public async Task DroppedConnection_IsDisposedAndReplaced()
    {
        var dead = ClosedConnection();
        var live = OpenConnection();
        var queue = new Queue<IConnection>([dead, live]);
        var source = new ProbeConnectionSource(() => Task.FromResult(queue.Dequeue()));

        await source.GetAsync();          // caches the (already closed) first connection
        var second = await source.GetAsync();

        second.Should().BeSameAs(live, "a closed connection must be replaced, not handed back");
        await dead.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentProbes_OpenOnlyOneConnection()
    {
        var connection = OpenConnection();
        var connects = 0;
        var source = new ProbeConnectionSource(async () =>
        {
            Interlocked.Increment(ref connects);
            await Task.Delay(20);        // widen the window where a racing caller could double-connect
            return connection;
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => source.GetAsync()));

        results.Should().OnlyContain(c => ReferenceEquals(c, connection));
        connects.Should().Be(1, "the gate must collapse a burst of probes onto a single connect");
    }
}
