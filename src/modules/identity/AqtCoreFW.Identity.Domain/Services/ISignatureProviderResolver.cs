namespace AqtCoreFW.Identity.Domain.Services;

public interface ISignatureProviderResolver
{
    /// <summary>
    /// Resolve a provider by key. Throws <see cref="KeyNotFoundException"/> when no provider
    /// is registered with <paramref name="providerKey"/>.
    /// Existing single-arg contract — unchanged since W1.
    /// </summary>
    IDigitalSignatureProvider Resolve(string providerKey);

    /// <summary>
    /// Capability-gated resolve. Returns the provider only if its
    /// <see cref="IDigitalSignatureProvider.Capabilities"/> satisfies the <paramref name="required"/>
    /// bitmask (bitwise AND: <c>(provider.Capabilities &amp; required) == required</c>).
    /// Throws <see cref="NotSupportedException"/> when the provider is found but lacks the required capability.
    /// Throws <see cref="KeyNotFoundException"/> when no provider matches <paramref name="providerKey"/>.
    ///
    /// <para>Use this overload when dispatching tier-specific operations to ensure the resolved
    /// provider actually supports the requested tier before invoking it. Example:
    /// <code>
    /// var provider = resolver.Resolve(tenantConfig.ProviderKey, SignatureProviderCapabilities.RawSign);
    /// </code>
    /// </para>
    ///
    /// <para>Implementation in W1 (<c>SignatureProviderResolver</c> in Identity.Infrastructure).</para>
    /// </summary>
    IDigitalSignatureProvider Resolve(string providerKey, SignatureProviderCapabilities required);

    IReadOnlyList<IDigitalSignatureProvider> GetAvailableProviders();
}
