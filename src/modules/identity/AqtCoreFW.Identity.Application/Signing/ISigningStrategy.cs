using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// A signing strategy owns one cell of the <c>(artifact kind × provider capabilities)</c> routing matrix.
///
/// <para>This interface is the routable predicate (<see cref="CanHandle"/>) only; the execution surface lives on
/// the separate <c>ISigningExecutionStrategy</c> marker. A strategy implements the execution surface only when its
/// artifact actually drives an async request through the orchestrator (e.g. the USB doc-flow round-trip). A
/// routing-only strategy is intentional, not a stub: some artifacts (detached form-field signatures) are verified
/// synchronously at ingest and never enter the orchestrator. Splitting routing from execution avoids throw-stubs
/// and avoids committing to an execution contract an artifact may never need.</para>
///
/// <para><b>Capability-exclusion.</b> Because <c>RawSign</c> is declared by BOTH the USB token and
/// Viettel, a server-side PDF strategy MUST match on <c>ServerSideSigning</c> AND exclude
/// <c>ClientSideSigning</c> in its <see cref="CanHandle"/> — otherwise it would also accept the USB provider and
/// later throw inside <c>SignAsync</c>. Each strategy encodes its own exclusions so the router resolves exactly
/// one strategy per cell.</para>
/// </summary>
public interface ISigningStrategy
{
    /// <summary>Stable identifier for diagnostics and ambiguity errors (e.g. "docflow", "detached", "pades").</summary>
    string Name { get; }

    /// <summary>
    /// True when this strategy owns the given artifact kind for a provider with the resolved capabilities.
    /// Implementations MUST be mutually exclusive across the matrix (exactly one strategy per valid cell).
    /// </summary>
    bool CanHandle(SigningArtifactKind artifactKind, SignatureProviderCapabilities resolvedCapabilities);
}
