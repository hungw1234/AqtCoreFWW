namespace AqtCoreFW.SharedKernel.Contracts;

/// <summary>
/// Service-to-service identity for outbound calls.
/// Monolith: NoOpServiceIdentityProvider (no inter-service auth needed).
/// Microservice: MtlsServiceIdentityProvider or ServiceJwtProvider.
/// Called by TenantPropagationHandler (Phase 02) on outbound HTTP calls.
/// </summary>
public interface IServiceIdentityProvider
{
    string ServiceId { get; }
    Task<string?> GetServiceTokenAsync(CancellationToken cancellationToken = default);
}
