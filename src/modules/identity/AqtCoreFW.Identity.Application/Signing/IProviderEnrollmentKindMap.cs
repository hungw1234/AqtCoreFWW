using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Resolves a signing <c>providerKey</c> to the <see cref="EnrollmentKind"/> a signer must hold an Approved
/// enrollment of to use that provider. Key-agnostic abstraction: this interface carries NO vendor key literals
/// (the D6 / QĐ742 mandate keeps Identity.Domain + Identity.Application provider-key-free). The concrete
/// key→kind table lives in the Infrastructure signing-adapter layer alongside the providers themselves; the
/// allowlist guard depends only on this contract.
/// </summary>
public interface IProviderEnrollmentKindMap
{
    /// <summary>
    /// Resolves the enrollment kind required for <paramref name="providerKey"/>. Returns false for a provider
    /// with no mapping — the guard treats a registered-but-unmapped provider as a fail-closed deny, never
    /// fail-open. An architecture drift test asserts every registered provider key is mapped.
    /// </summary>
    bool TryResolve(string providerKey, out EnrollmentKind kind);
}
