namespace AqtCoreFW.SharedKernel.Contracts;

/// <summary>
/// Default implementation for monolith — no service-to-service auth needed.
/// Replace with MtlsServiceIdentityProvider when extracting microservices.
/// </summary>
public sealed class NoOpServiceIdentityProvider : IServiceIdentityProvider
{
    public string ServiceId => "monolith";

    public Task<string?> GetServiceTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
