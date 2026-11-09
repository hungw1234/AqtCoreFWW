namespace AqtCoreFW.SharedKernel.Abstractions;

/// <summary>
/// Centralized Guid generation. DI-injected so tests can substitute deterministic implementations.
/// Phase-2 Foundation Refactor (2026-05-03): dual API — v7 monotonic (default, time-ordering benefit)
/// and v4 random (sensitive PII entities — FormSubmission only in current codebase per Phase-1 sensitivity list).
/// Phase-3 architecture test bans direct Guid.NewGuid()/Guid.CreateVersion7() usage outside V7GuidGenerator implementation.
/// </summary>
public interface IGuidGenerator
{
    /// <summary>v7 monotonic — timestamp-prefixed Guid (RFC 9562). Use for default ordering benefit (clustered key locality, FIFO dispatch).</summary>
    Guid NewIdMonotonic();

    /// <summary>v4 random — fully random Guid. Use for sensitive entities where timestamp leak in id is a privacy concern (e.g. FormSubmission with PII payload).</summary>
    Guid NewIdRandom();
}
