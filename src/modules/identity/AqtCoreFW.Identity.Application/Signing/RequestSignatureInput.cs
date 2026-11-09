using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Provider-agnostic input to <see cref="ISignatureOrchestrator.RequestSignatureAsync"/>.
///
/// <para><see cref="ArtifactKind"/> is server-derived by the caller's trusted context (form-field type for
/// data, %PDF magic-byte / generation source for PDF) — it is NOT a free-form value sniffed from the client.
/// The orchestrator routes on it together with the resolved provider capabilities. No signing certificate is
/// supplied here: for USB doc-flow the cert is only known post-upload (bound via
/// <see cref="ISignerBindingGuard.VerifyAsync"/>), and for remote it arrives with the
/// signature.</para>
/// </summary>
public sealed record RequestSignatureInput(
    Guid TenantId,
    SigningArtifactKind ArtifactKind,
    string ArtifactRef,
    string ProviderKey,
    Guid AssignedSignerId,
    Guid InitiatedBy,
    string? Placement = null,
    DocFlowSignParameters? DocFlow = null,
    /// <summary>
    /// Position of this request in an ordered multi-signer chain (1-based, default 1). The orchestrator
    /// threads this into the created <see cref="SigningRequest"/> and the strategy passes it into
    /// <see cref="InitiateDocFlowCommand"/>. Existing single-sign callers omit it and get 1.
    /// </summary>
    int SignerSequenceNumber = 1,
    /// <summary>
    /// For UploadedPdf kind only: the PublicId of the Files module FileRecord that was staged for signing.
    /// Stored on the SigningRequest so the Files module can query signing steps without a column on FileRecord.
    /// Null for FormPdf and DetachedData kinds.
    /// </summary>
    Guid? SourceFileId = null,
    /// <summary>
    /// Strategy-specific parameters for the server-side PAdES path (remote Viettel Mysign). Supplied when the
    /// artifact routes to the <c>ServerSideSigning</c> assembler; ignored by the doc-flow / detached strategies.
    /// </summary>
    PadesSignParameters? Pades = null);

/// <summary>
/// Strategy-specific parameters for the server-side PAdES path. Supplied when
/// <see cref="RequestSignatureInput.ArtifactKind"/> is a PDF kind routed to a
/// <c>ServerSideSigning | RawSign</c> provider (remote Viettel Mysign); ignored by other strategies.
///
/// <para>The visible-stamp placement (image + text rect) is NOT carried here — it travels on
/// <see cref="RequestSignatureInput.Placement"/> (JSON), which the PAdES initiate path parses to compose a
/// visible stamp; an absent/blank placement signs invisibly (PAdES-B-B). These params only carry the staged
/// original's key. <see cref="DocumentName"/> is the free-text reason shown on the signer's mobile app during
/// the OTP confirmation (and reused as the stamp's reason line).</para>
/// </summary>
/// <param name="OriginalStorageKey">Tenant-prefixed key of the unsigned PDF in the signing-original bucket.</param>
/// <param name="DocumentName">Optional label shown on the signer's Mysign mobile app; null → a default.</param>
/// <param name="SignFlowModeOverride">
/// Per-request override of the remote signing flow (Sync / Async). When supplied it wins over the tenant's
/// <see cref="TenantSignatureConfig.SignFlowMode"/>; null → fall back to the tenant config (which itself defaults
/// to Async). Used by the dev demo card to exercise either flow without re-configuring the tenant.
/// </param>
/// <param name="Reason">
/// Optional user-entered reason rendered on the visible stamp's "Lý do" line. Distinct from
/// <see cref="DocumentName"/> (the Mysign app label). Null/blank → the stamp omits the reason line entirely.
/// </param>
public sealed record PadesSignParameters(
    string OriginalStorageKey,
    string? DocumentName = null,
    SignatureFlowMode? SignFlowModeOverride = null,
    string? Reason = null);

/// <summary>
/// Strategy-specific parameters for the USB doc-flow path. Supplied when <see cref="RequestSignatureInput.ArtifactKind"/>
/// is a PDF kind routed to a <c>ClientSideSigning | DocFlow</c> provider; ignored by other strategies.
///
/// <para>The doc-flow strategy threads these into the existing <c>InitiateDocFlowCommand</c> so the round-trip
/// reuses that command's tenant-prefix validation (the storage-key cross-tenant guard) and audit — the unified
/// path must NOT re-implement those.</para>
/// </summary>
/// <param name="SignType">Which VGCA plugin function to drive (Issued / Approved / Income / …).</param>
/// <param name="OriginalStorageKey">Tenant-prefixed key of the unsigned PDF in the signing-original bucket.</param>
/// <param name="DocNumber">Document reference number shown in the visible stamp.</param>
/// <param name="IssuedDate">Issuance date shown in the visible stamp.</param>
/// <param name="BaseUrl">API host base URL used to build the VGCA handler callback URL.</param>
/// <param name="FileName">Optional forced storage filename (null → auto).</param>
/// <param name="Extra">Optional extra stamp metadata.</param>
/// <param name="PreviousSignedStorageKey">
/// For incremental multi-signer chaining: the storage key (in bucket "signing-docflow") of the signed
/// PDF produced by the preceding signer. Null for sequence 1; required and validated by
/// <see cref="InitiateDocFlowCommand"/> for sequence &gt; 1. The desktop tool appends an incremental
/// PAdES signature to this artifact without touching prior signers' signatures.
/// </param>
/// <param name="SignerSequenceNumber">Position in an ordered chain (1-based, default 1).</param>
public sealed record DocFlowSignParameters(
    DocFlowSignType SignType,
    string OriginalStorageKey,
    string DocNumber,
    DateTime IssuedDate,
    string BaseUrl,
    string? FileName = null,
    IReadOnlyDictionary<string, string>? Extra = null,
    string? PreviousSignedStorageKey = null,
    int SignerSequenceNumber = 1);
