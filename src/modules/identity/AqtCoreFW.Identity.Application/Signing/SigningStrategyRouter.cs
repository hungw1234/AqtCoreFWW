using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Resolves the provider by key (capabilities come from the provider, never a key-string branch) and selects
/// the single strategy that owns the <c>(artifactKind × capabilities)</c> cell.
///
/// <para>Pure (depends only on <see cref="ISignatureProviderResolver"/> + the registered strategies) so the
/// full routing matrix is unit-testable without infrastructure. Resolution is fail-loud: zero matches →
/// <see cref="NotSupportedException"/>; more than one → <see cref="InvalidOperationException"/> (a registration
/// bug — the matrix must be mutually exclusive).</para>
/// </summary>
public sealed class SigningStrategyRouter(
    ISignatureProviderResolver providerResolver,
    IEnumerable<ISigningStrategy> strategies)
{
    private readonly IReadOnlyList<ISigningStrategy> _strategies = strategies.ToList();

    /// <summary>
    /// Routes the request. Resolves the provider (throws <see cref="KeyNotFoundException"/> if the key is
    /// unknown), then matches exactly one strategy against the artifact kind and the provider's capabilities.
    /// </summary>
    public SigningRoute Route(SigningArtifactKind artifactKind, string providerKey)
    {
        var provider = providerResolver.Resolve(providerKey);
        var caps = provider.Capabilities;

        var matches = _strategies.Where(s => s.CanHandle(artifactKind, caps)).ToList();

        if (matches.Count == 0)
            throw new NotSupportedException(
                $"No signing strategy handles artifact '{artifactKind}' for provider '{providerKey}' " +
                $"(capabilities: {caps}).");
        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Ambiguous signing route for artifact '{artifactKind}' / provider '{providerKey}': " +
                $"{string.Join(", ", matches.Select(m => m.Name))}. The routing matrix must be mutually exclusive.");

        return new SigningRoute(matches[0], provider);
    }
}

/// <summary>The strategy + resolved provider chosen for a request.</summary>
public sealed record SigningRoute(ISigningStrategy Strategy, IDigitalSignatureProvider Provider);
