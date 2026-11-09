using FluentResults;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Provider-agnostic signing facade. Callers (BP step, form-PDF flow, form field) issue one request; the
/// orchestrator routes by <c>(artifact kind × resolved provider capabilities)</c> to a strategy and exposes a
/// pollable, read-time-projected status. The two existing state machines
/// (<c>SigningDocFlowRecord</c> / <c>SigningPendingTransaction</c>) stay the source of truth underneath.
/// </summary>
public interface ISignatureOrchestrator
{
    /// <summary>
    /// Creates a durable, resumable <c>SigningRequest</c>, routes it to the strategy that owns its
    /// <c>(artifactKind × capabilities)</c> cell, executes that strategy, and links the underlying provider
    /// record atomically with the request. Expected failures (unsupported route, matched strategy has no
    /// execution surface yet, bad input, an underlying command rejected by its validator) come back as a failed
    /// <see cref="Result{T}"/> with the error type preserved so the API maps 400/422 — not a generic 500. On
    /// success: the request's public id (for polling) plus the strategy's client hand-off (doc-flow URL/JWT/docId).
    /// </summary>
    Task<Result<RequestSignatureResult>> RequestSignatureAsync(RequestSignatureInput input, CancellationToken ct = default);

    /// <summary>
    /// Read-time status projection. Loads the tenant-scoped request and maps its underlying record's state to
    /// <c>SigningRequestStatus</c> (or the unlinked/creation-failure state). Returns a failed
    /// <see cref="Result{T}"/> carrying a <c>NotFoundError</c> (→ 404) when no request matches the public id in
    /// the tenant.
    /// </summary>
    Task<Result<SigningRequestStatusDto>> GetStatusAsync(Guid publicId, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Returns the assembled signed PDF bytes for a Completed server-side PAdES request (a request backed by a
    /// <c>SigningPendingTransaction</c> whose completion path stored the spliced PDF in the pades bucket). Tenant-
    /// scoped: a request from another tenant yields <c>NotFoundError</c> (→ 404). A request that is not a PAdES
    /// request, or not yet Completed, yields a <c>BadRequestError</c> (→ 400) — the caller should keep polling
    /// status. This is the read path the FE uses after <see cref="GetStatusAsync"/> reports Completed.
    /// </summary>
    Task<Result<SignedPdfDownload>> GetSignedPdfAsync(Guid publicId, Guid tenantId, CancellationToken ct = default);
}

/// <summary>The assembled signed PDF bytes + a download file name for a Completed PAdES request.</summary>
public sealed record SignedPdfDownload(byte[] Content, string FileName);
