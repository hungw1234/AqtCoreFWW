namespace AqtCoreFW.Infrastructure.Resilience;

/// <summary>Snapshot of a named circuit breaker's current state — exposed via Admin API.</summary>
public record CircuitBreakerStatus(
    string Name,
    string State,        // Closed | Open | HalfOpen
    double FailureRate,
    DateTimeOffset LastStateChange);
