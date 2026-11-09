using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Strategies;

/// <summary>
/// Detached/data signature over raw bytes (form field value) — no PDF assembly. Works on either provider that
/// can produce a raw hash signature (<see cref="SignatureProviderCapabilities.RawSign"/>), USB or remote.
///
/// <para>Routing-only by design — no execution surface. Detached form-field signatures arrive complete with the
/// form submission and are verified synchronously at ingest (the verify-at-ingest path in the Forms submit
/// handlers, where the signer-cert→assigned-signer binding is enforced). There is no async request to drive, no
/// durable underlying record to link or poll, so the detached flow never enters the orchestrator: this strategy
/// declares its routing cell but deliberately does not implement <c>ISigningExecutionStrategy</c>.</para>
/// </summary>
public sealed class DetachedDataSigningStrategy : ISigningStrategy
{
    public string Name => "detached";

    public bool CanHandle(SigningArtifactKind artifactKind, SignatureProviderCapabilities resolvedCapabilities)
        => artifactKind == SigningArtifactKind.DetachedData
           && resolvedCapabilities.HasFlag(SignatureProviderCapabilities.RawSign);
}
