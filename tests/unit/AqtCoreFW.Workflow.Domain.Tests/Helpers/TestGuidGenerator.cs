using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Workflow.Domain.Tests.Helpers;

/// <summary>
/// Simple IGuidGenerator for domain unit tests.
/// Each call to NewIdMonotonic/NewIdRandom returns a fresh deterministic Guid.
/// Thread-safe via Interlocked counter.
/// </summary>
internal sealed class TestGuidGenerator : IGuidGenerator
{
    // Shared across all instances so each Create() returns generator that produces unique values.
    // Instance-level counter caused Create() per call-site to restart at 0, yielding identical Guids.
    private static int _counter;

    public Guid NewIdMonotonic() =>
        new Guid(Interlocked.Increment(ref _counter), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public Guid NewIdRandom() =>
        new Guid(Interlocked.Increment(ref _counter), 1, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static TestGuidGenerator Create() => new();
}
