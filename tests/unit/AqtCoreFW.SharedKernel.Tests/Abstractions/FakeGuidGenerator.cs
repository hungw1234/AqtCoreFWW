using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.SharedKernel.Tests.Abstractions;

/// <summary>
/// Deterministic IGuidGenerator for tests. Emits a sequence of Guids supplied at construction
/// (separate sequences for monotonic and random calls). Once the sequence is exhausted, throws.
///
/// Phase-2 Foundation Refactor (2026-05-03). When multiple test projects need this, promote to a
/// shared TestKit assembly (deferred — YAGNI until 2nd consumer).
/// </summary>
public sealed class FakeGuidGenerator : IGuidGenerator
{
    private readonly Queue<Guid> _monotonicQueue;
    private readonly Queue<Guid> _randomQueue;

    public FakeGuidGenerator(IEnumerable<Guid>? monotonic = null, IEnumerable<Guid>? random = null)
    {
        _monotonicQueue = new Queue<Guid>(monotonic ?? Array.Empty<Guid>());
        _randomQueue = new Queue<Guid>(random ?? Array.Empty<Guid>());
    }

    /// <summary>Convenience: returns a generator that emits sequential Guids derived from int counters (deterministic across runs).</summary>
    public static FakeGuidGenerator Sequential(int monotonicCount = 16, int randomCount = 16)
    {
        var monotonic = Enumerable.Range(1, monotonicCount).Select(i => new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var random = Enumerable.Range(1001, randomCount).Select(i => new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        return new FakeGuidGenerator(monotonic, random);
    }

    public Guid NewIdMonotonic()
    {
        if (_monotonicQueue.Count == 0)
            throw new InvalidOperationException("FakeGuidGenerator: monotonic sequence exhausted.");
        return _monotonicQueue.Dequeue();
    }

    public Guid NewIdRandom()
    {
        if (_randomQueue.Count == 0)
            throw new InvalidOperationException("FakeGuidGenerator: random sequence exhausted.");
        return _randomQueue.Dequeue();
    }
}
