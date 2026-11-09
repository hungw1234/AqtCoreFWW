namespace AqtCoreFW.Infrastructure.Resilience;

/// <summary>
/// Registry for monitoring named circuit breaker states.
/// Updated by Polly state-change callbacks; queried by Admin API (Phase 06).
/// </summary>
public interface ICircuitBreakerRegistry
{
    IEnumerable<CircuitBreakerStatus> GetAll();
    void Register(string name);
    void UpdateState(string name, string state, double failureRate);
}
