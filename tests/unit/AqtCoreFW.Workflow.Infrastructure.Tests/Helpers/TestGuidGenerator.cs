using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Helpers;

/// <summary>
/// Deterministic IGuidGenerator for unit tests.
/// Uses a process-wide atomic counter (not per-instance) so IDs are unique
/// across all test class instances sharing the same test run — required for
/// integration tests that share a single SQL Server schema via SqlServerFixture.
/// Thread-safe via Interlocked.
/// </summary>
internal sealed class TestGuidGenerator : IGuidGenerator
{
    // Process-wide counter — avoids duplicate PublicIds in integration tests that share SQL Server
    private static int _globalCounter;

    public Guid NewIdMonotonic() =>
        new Guid(Interlocked.Increment(ref _globalCounter), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public Guid NewIdRandom() =>
        new Guid(Interlocked.Increment(ref _globalCounter), 1, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static TestGuidGenerator Create() => new();
}
