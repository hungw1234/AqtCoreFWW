using MassTransit;

namespace AqtCoreFW.Messaging.Integration.Tests.Fixtures;

// ---------------------------------------------------------------------------
// Test-only message contracts — not part of production domain
// ---------------------------------------------------------------------------

/// <summary>Simple test event published to verify consumer receives message.</summary>
public sealed record OrderPlacedEvent(Guid OrderId, string CustomerName, string CorrelationId);

/// <summary>Command that always throws — used to trigger dead-letter flow.</summary>
public sealed record AlwaysFailCommand(Guid CommandId, string Reason);

// ---------------------------------------------------------------------------
// Test consumers
// ---------------------------------------------------------------------------

/// <summary>
/// Successful consumer — signals receipt via TaskCompletionSource.
/// Used in TC-MSG-INT-001 and TC-MSG-INT-004.
/// </summary>
public sealed class TestOrderConsumer : IConsumer<OrderPlacedEvent>
{
    // Static TCS so tests can await a single message receipt
    private static TaskCompletionSource<OrderPlacedEvent> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Reset before each test that uses this consumer.</summary>
    public static void Reset() =>
        _tcs = new TaskCompletionSource<OrderPlacedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Await the next message received by this consumer (up to the given timeout).</summary>
    public static Task<OrderPlacedEvent> WaitForMessageAsync(TimeSpan timeout) =>
        _tcs.Task.WaitAsync(timeout);

    public Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        _tcs.TrySetResult(context.Message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Always-failing consumer — throws on every consume attempt.
/// Used in TC-MSG-INT-002 to drive the dead-letter flow.
/// </summary>
public sealed class TestFaultingConsumer : IConsumer<AlwaysFailCommand>
{
    public Task Consume(ConsumeContext<AlwaysFailCommand> context) =>
        throw new InvalidOperationException($"Intentional failure: {context.Message.Reason}");
}
