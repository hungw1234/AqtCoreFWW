using System.Collections.Concurrent;

namespace AqtCoreFW.Infrastructure.Resilience;

/// <summary>
/// In-memory circuit breaker registry — thread-safe via ConcurrentDictionary.
/// Polly state-change callbacks call UpdateState(); Admin API reads GetAll().
/// </summary>
public sealed class InMemoryCircuitBreakerRegistry : ICircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, CircuitBreakerStatus> _statuses = new();

    public IEnumerable<CircuitBreakerStatus> GetAll() => _statuses.Values;

    public void Register(string name) =>
        _statuses.TryAdd(name, new CircuitBreakerStatus(name, "Closed", 0.0, DateTimeOffset.UtcNow));

    public void UpdateState(string name, string state, double failureRate) =>
        _statuses[name] = new CircuitBreakerStatus(name, state, failureRate, DateTimeOffset.UtcNow);
}
