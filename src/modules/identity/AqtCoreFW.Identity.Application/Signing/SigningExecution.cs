using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Domain.Entities;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Execution surface for a routed signing strategy. Kept SEPARATE from <see cref="ISigningStrategy"/> (which
/// declares only the routing predicate) so that strategies whose execution is not yet implemented expose no
/// throw-stub: they implement <see cref="ISigningStrategy"/> for routing and simply do NOT implement this
/// interface until their owning phase adds the execution path. The orchestrator resolves the strategy by
/// <see cref="ISigningStrategy.CanHandle"/>, then surfaces a clear <see cref="NotSupportedException"/> if the
/// matched strategy has no execution surface yet.
/// </summary>
public interface ISigningExecutionStrategy : ISigningStrategy
{
    /// <summary>
    /// Performs the provider-specific initiation for an already-routed request and returns the underlying
    /// provider record to link plus the client hand-off the caller relays to the front end. MUST NOT create or
    /// persist the <see cref="SigningRequest"/> aggregate — the orchestrator owns that lifecycle (create → link →
    /// save) so linkage is atomic with respect to the request row.
    ///
    /// <para>Returns a <see cref="Result{T}"/> so expected client/validation failures (bad input, an underlying
    /// command rejected by its validator) flow back with their FluentResults error TYPE intact — the controller's
    /// <c>ToApiResponse</c> then maps them to 400/422 rather than a generic 500. Truly unexpected invariant
    /// violations (e.g. a successful initiation that returned no record id) still throw.</para>
    /// </summary>
    Task<Result<SigningExecutionResult>> ExecuteAsync(SigningExecutionContext context, CancellationToken ct = default);
}

/// <summary>Inputs handed to a strategy's <see cref="ISigningExecutionStrategy.ExecuteAsync"/>.</summary>
/// <param name="Input">The original provider-agnostic request input.</param>
/// <param name="Provider">The provider the router resolved for this request (capabilities already validated).</param>
public sealed record SigningExecutionContext(
    RequestSignatureInput Input,
    IDigitalSignatureProvider Provider);

/// <summary>
/// What a strategy produced: the underlying provider record to link to the durable request, and the transport
/// hand-off the FE needs to drive the ceremony (e.g. doc-flow handler URL + JWT + docId).
/// </summary>
/// <param name="UnderlyingKind">Which provider state machine backs this request.</param>
/// <param name="UnderlyingRecordId">Stable id of that record (DocFlow record PK as string / remote txn id).</param>
/// <param name="ClientHandoff">Opaque string map relayed verbatim to the FE; empty when nothing is handed off.</param>
public sealed record SigningExecutionResult(
    SigningUnderlyingKind UnderlyingKind,
    string UnderlyingRecordId,
    IReadOnlyDictionary<string, string> ClientHandoff);

/// <summary>
/// Result of <see cref="ISignatureOrchestrator.RequestSignatureAsync"/>: the durable request's public id (for
/// polling <see cref="ISignatureOrchestrator.GetStatusAsync"/>) plus the strategy's client hand-off.
/// </summary>
public sealed record RequestSignatureResult(
    Guid PublicId,
    IReadOnlyDictionary<string, string> ClientHandoff);
