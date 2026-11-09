using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Workflow.Application.Tests.Helpers;

/// <summary>
/// Simple IGuidGenerator for application unit tests.
/// Each call to NewIdMonotonic/NewIdRandom returns a fresh deterministic Guid.
/// Thread-safe via Interlocked counter.
/// </summary>
internal sealed class TestGuidGenerator : IGuidGenerator
{
    // Shared across all instances — instance-level counter caused identical Guids when Create() was called per call-site.
    private static int _counter;

    public Guid NewIdMonotonic() =>
        new Guid(Interlocked.Increment(ref _counter), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public Guid NewIdRandom() =>
        new Guid(Interlocked.Increment(ref _counter), 1, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static TestGuidGenerator Create() => new();
}
