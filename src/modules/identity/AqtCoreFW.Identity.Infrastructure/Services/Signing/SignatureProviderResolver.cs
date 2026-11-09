using AqtCoreFW.Identity.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Resolves <see cref="IDigitalSignatureProvider"/> instances by provider key using keyed DI.
///
/// D6 extensibility contract (QĐ 742 §5.1): adding a new VN CA provider requires:
///   1. Implement <see cref="IDigitalSignatureProvider"/>
///   2. Register: services.AddKeyedScoped&lt;IDigitalSignatureProvider, T&gt;(key)
///              + services.AddScoped&lt;IDigitalSignatureProvider, T&gt;()  ← for GetAvailableProviders()
///   3. Zero changes to core (resolver, handlers, controllers).
///
/// Resolve() throws KeyNotFoundException when the key is not registered — callers must handle this
/// as a 503/bad-config scenario (no provider registered for tenant's configured ProviderKey).
///
/// GetAvailableProviders() enumerates all providers via IEnumerable&lt;IDigitalSignatureProvider&gt;
/// from DI — automatically discovers any registered provider without core changes.
/// </summary>
public sealed class SignatureProviderResolver(
    IServiceProvider sp,
    IEnumerable<IDigitalSignatureProvider> allProviders) : ISignatureProviderResolver
{
    // Cache the provider list — IEnumerable is resolved once at construction (Scoped lifetime).
    private readonly IReadOnlyList<IDigitalSignatureProvider> _providers =
        allProviders.ToList();

    /// <inheritdoc/>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no provider is registered for <paramref name="providerKey"/>.
    /// Callers should surface this as a 503 Service Unavailable or configuration error.
    /// </exception>
    public IDigitalSignatureProvider Resolve(string providerKey)
    {
        var provider = sp.GetKeyedService<IDigitalSignatureProvider>(providerKey);
        if (provider is null)
        {
            var known = _providers.Count > 0
                ? string.Join(", ", _providers.Select(p => p.ProviderKey))
                : "(none registered)";
            throw new KeyNotFoundException(
                $"No IDigitalSignatureProvider registered for key '{providerKey}'. " +
                $"Registered keys: {known}");
        }
        return provider;
    }

    /// <inheritdoc/>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no provider is registered for <paramref name="providerKey"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when the provider is found but its <see cref="IDigitalSignatureProvider.Capabilities"/>
    /// do not satisfy the <paramref name="required"/> bitmask.
    /// Full capability-aware dispatch wired in W1.
    /// </exception>
    public IDigitalSignatureProvider Resolve(string providerKey, SignatureProviderCapabilities required)
    {
        var provider = Resolve(providerKey);
        if ((provider.Capabilities & required) != required)
        {
            throw new NotSupportedException(
                $"Provider '{providerKey}' does not support required capabilities {required}. " +
                $"Provider capabilities: {provider.Capabilities}");
        }
        return provider;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Enumerates from DI — any provider registered as unkeyed IDigitalSignatureProvider appears here.
    /// Zero-touch extensibility: adding a new provider via DI registration makes it auto-discoverable.
    /// </remarks>
    public IReadOnlyList<IDigitalSignatureProvider> GetAvailableProviders() => _providers;
}
