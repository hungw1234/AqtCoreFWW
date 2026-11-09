namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module façade: allows Forms (and any other module) to initiate a signing request through
/// the Identity signing orchestrator without referencing Identity.Application or Identity.Infrastructure.
///
/// Monolith: <c>InProcessSigningRequestClient</c> in Identity.Infrastructure maps Contracts-owned DTOs
/// to <c>RequestSignatureInput</c> / <c>RequestSignatureResult</c> in Identity.Application.Signing.
/// Microservice: gRPC client when Identity is extracted.
///
/// Contracts cannot reference Identity.Application, so request/result shapes are mirrored here and
/// mapped inside the in-process implementation. Uses plain discriminated-record outcomes (no FluentResults
/// on the contract surface) compatible with future gRPC / JSON transport.
/// </summary>
public interface ISigningRequestClient
{
    /// <summary>
    /// Initiates a durable signing request through the orchestrator. The orchestrator routes by
    /// <c>(artifactKind × resolved provider capabilities)</c>, creates the underlying provider record,
    /// and returns the durable request's public id (for polling) plus the strategy's client hand-off.
    ///
    /// <para>Expected failures (unsupported route, bad DocFlow params, sequence-predecessor not completed)
    /// come back as a failed <see cref="SigningRequestOutcome"/> with a typed error code — never a
    /// generic 500. The caller maps the error code to an appropriate HTTP status (400/422/404).</para>
    /// </summary>
    Task<SigningRequestOutcome> RequestSignatureAsync(
        SigningRequestClientInput input,
        CancellationToken ct = default);

    // ─── Read surface (cross-module, tenant-asserted) ─────────────────────────────

    /// <summary>
    /// Returns all signing steps for the given uploaded file, ordered by signer sequence number.
    /// Tenant-scoped: only requests belonging to <paramref name="tenantId"/> are returned.
    /// Returns an empty list when no signing request has been created for the file (not an error).
    /// </summary>
    Task<IReadOnlyList<SigningStepDto>> GetStepsForFileAsync(
        Guid tenantId, Guid sourceFileId, CancellationToken ct = default);

    /// <summary>
    /// Returns all signing steps for the signing request identified by its public id.
    /// Used by the Forms module which stores <c>FormSubmission.SigningRequestId</c> (= request PublicId).
    /// Tenant-scoped; returns empty list when no matching request found (no signing yet or null id).
    /// </summary>
    Task<IReadOnlyList<SigningStepDto>> GetStepsForRequestPublicIdAsync(
        Guid tenantId, Guid requestPublicId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the signed storage key for a specific step of an uploaded file so the Files module
    /// can stream the signed PDF without the client ever knowing the bucket or key.
    /// Tenant-scoped + only returns the key when the step is Completed.
    /// </summary>
    Task<SignedStepResolution> ResolveSignedStepForFileAsync(
        Guid tenantId, Guid sourceFileId, int sequenceNumber, CancellationToken ct = default);

    /// <summary>
    /// Resolves the signed storage key for one specific signing attempt of an uploaded file, addressed by
    /// the SigningRequest PublicId rather than by sequence number. This is the addressing the Files download
    /// surface uses so that each independent sign of the same file (which all share sequence 1) resolves to
    /// its own signed PDF blob instead of always returning the latest. The request must belong to both
    /// <paramref name="tenantId"/> and <paramref name="sourceFileId"/> (cross-tenant / cross-file → NOT_FOUND);
    /// only returns the key when the attempt is Completed.
    /// </summary>
    Task<SignedStepResolution> ResolveSignedStepForFileByRequestAsync(
        Guid tenantId, Guid sourceFileId, Guid requestPublicId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the signed storage key for a specific step of a signing request (by public id).
    /// Used by the Forms module; tenant-scoped + only returns the key when the step is Completed.
    /// </summary>
    Task<SignedStepResolution> ResolveSignedStepForRequestAsync(
        Guid tenantId, Guid requestPublicId, int sequenceNumber, CancellationToken ct = default);

    /// <summary>
    /// Batch query: returns the subset of <paramref name="sourceFileIds"/> that have at least one
    /// Completed signed step. Used by ListFiles to set the isDigitallySigned badge without N+1 queries.
    /// Tenant-scoped.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetSignedFileIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> sourceFileIds, CancellationToken ct = default);
}

// ─── Read DTOs ────────────────────────────────────────────────────────────────

/// <summary>
/// One signing step (= one SigningRequest row) projected for the FE signing-steps panel.
/// Shared shape used by both the Files and Forms read surfaces (same JSON shape from both endpoints).
/// </summary>
public sealed record SigningStepDto(
    /// <summary>
    /// Stable per-attempt identifier = the SigningRequest PublicId. Distinguishes independent signs of the
    /// same file: when one file is signed multiple times every sign is its own request (and its own signed
    /// PDF blob), so the FE keys rows and addresses blob downloads by this id rather than by sequence number
    /// (a single-signer file re-signed N times produces N rows that all share sequence 1).
    /// </summary>
    Guid SigningRequestId,
    /// <summary>Position in the signer chain (1-based).</summary>
    int SequenceNumber,
    /// <summary>Read-time projected status string: Pending | InProgress | Completed | Failed | Expired.</summary>
    string Status,
    /// <summary>UserId of the assigned signer; null if not yet set (should not occur in practice).</summary>
    Guid? SignerUserId,
    /// <summary>Display name of the signer if available via an existing Identity lookup; null otherwise.</summary>
    string? SignerName,
    /// <summary>UTC timestamp when signing completed; null until Completed.</summary>
    DateTime? CompletedAt,
    /// <summary>True when a signed PDF storage key is available (DocFlowRecord.SignedStorageKey is set).</summary>
    bool HasSignedPdf);

/// <summary>
/// Resolution result for streaming a signed step's PDF. The consuming module endpoint calls
/// <see cref="IFileStorageService.DownloadAsync"/> with <see cref="Bucket"/> + <see cref="StorageKey"/>
/// — the client never receives these values.
/// </summary>
public sealed record SignedStepResolution(
    /// <summary>True when a signed key was located and the step is Completed.</summary>
    bool Found,
    /// <summary>Storage bucket name (e.g. "signing-docflow"). Null when Found=false.</summary>
    string? Bucket,
    /// <summary>Storage object key. Null when Found=false.</summary>
    string? StorageKey,
    /// <summary>Suggested download filename (derived from the original doc context). Null when Found=false.</summary>
    string? FileName,
    /// <summary>Machine-readable error code when Found=false (e.g. "NOT_COMPLETED", "NOT_FOUND", "CROSS_TENANT").</summary>
    string? ErrorCode);

// ─── Input DTO ────────────────────────────────────────────────────────────────

/// <summary>
/// Contracts-layer input for <see cref="ISigningRequestClient.RequestSignatureAsync"/>.
///
/// <para>Mirrors the fields of <c>RequestSignatureInput</c> (in Identity.Application.Signing) and
/// <c>DocFlowSignParameters</c> that a Forms consumer must supply. Contracts cannot reference
/// Identity.Application, so the Application input is reconstructed inside the in-process impl from
/// these fields. Enums that live in Identity.Domain are mirrored here as string constants to keep
/// the Contracts project free of all Identity module references.</para>
///
/// <para><c>ArtifactKind</c> is server-derived by the caller from trusted context (e.g. "FormPdf"
/// for a QuestPDF-generated submission PDF) — it is NOT a free-form value from the HTTP client.</para>
/// </summary>
public sealed class SigningRequestClientInput
{
    /// <summary>Tenant the request belongs to. Asserted explicitly; do not rely on RLS.</summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Kind of artifact being signed as a string constant matching <c>SigningArtifactKind</c>
    /// ("DetachedData" | "FormPdf" | "UploadedPdf"). Using a string keeps Contracts free of
    /// Identity.Domain enum references.
    /// </summary>
    public string ArtifactKind { get; init; } = string.Empty;

    /// <summary>
    /// Stable reference to the artifact within the signing pipeline. For DocFlow PDF kinds this is
    /// the docId Guid (as a lowercase string) that the VGCA desktop tool will reference; the unsigned
    /// PDF itself is referenced by <see cref="DocFlow"/>.<see cref="DocFlowSignClientParameters.OriginalStorageKey"/>.
    /// </summary>
    public string ArtifactRef { get; init; } = string.Empty;

    /// <summary>Signing provider key (e.g. "usb-vgca") resolved from the tenant's configuration.</summary>
    public string ProviderKey { get; init; } = string.Empty;

    /// <summary>UserId of the signer this request is assigned to (anti-impersonation invariant).</summary>
    public Guid AssignedSignerId { get; init; }

    /// <summary>UserId of the authenticated user initiating the request.</summary>
    public Guid InitiatedBy { get; init; }

    /// <summary>Optional placement JSON (anchor coordinates for visible signature stamp). Null when not used.</summary>
    public string? Placement { get; init; }

    /// <summary>
    /// DocFlow-specific parameters. Required when <see cref="ArtifactKind"/> routes to a
    /// <c>ClientSideSigning | DocFlow</c> provider; null for other strategy paths.
    /// </summary>
    public DocFlowSignClientParameters? DocFlow { get; init; }

    /// <summary>
    /// Position of this request in an ordered multi-signer chain (1-based, default 1).
    /// Single-sign callers omit it; the impl defaults to 1.
    /// </summary>
    public int SignerSequenceNumber { get; init; } = 1;

    /// <summary>
    /// For UploadedPdf artifact kind only: the PublicId (Guid) of the Files module FileRecord that was
    /// staged for signing. Null for FormPdf and DetachedData kinds.
    /// Forwarded to SigningRequest.Create so the Files module can query steps by file id.
    /// </summary>
    public Guid? SourceFileId { get; init; }
}

/// <summary>
/// Contracts-layer mirror of <c>DocFlowSignParameters</c> (Identity.Application.Signing). Kept here
/// so Forms can supply DocFlow-specific values without referencing the Application layer.
/// </summary>
public sealed class DocFlowSignClientParameters
{
    /// <summary>
    /// Which VGCA plugin function to drive, as a string matching <c>DocFlowSignType</c>
    /// ("Issued" | "Approved" | "Income" | "Comment" | "Appendix" | "Copy" | "Files").
    /// </summary>
    public string SignType { get; init; } = string.Empty;

    /// <summary>Tenant-prefixed storage key of the unsigned PDF in the signing-original bucket.</summary>
    public string OriginalStorageKey { get; init; } = string.Empty;

    /// <summary>Document reference number shown in the visible VGCA stamp.</summary>
    public string DocNumber { get; init; } = string.Empty;

    /// <summary>Issuance date shown in the visible VGCA stamp.</summary>
    public DateTime IssuedDate { get; init; }

    /// <summary>API host base URL used by the strategy to build the VGCA handler callback URL.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Optional forced storage filename. Null → strategy auto-generates.</summary>
    public string? FileName { get; init; }

    /// <summary>Optional extra stamp metadata key/value pairs.</summary>
    public IReadOnlyDictionary<string, string>? Extra { get; init; }

    /// <summary>
    /// For incremental multi-signer chaining: the storage key of the signed PDF produced by the
    /// preceding signer. Null for sequence 1; required for sequence &gt; 1.
    /// </summary>
    public string? PreviousSignedStorageKey { get; init; }
}

// ─── Outcome DTO ──────────────────────────────────────────────────────────────

/// <summary>
/// Outcome of <see cref="ISigningRequestClient.RequestSignatureAsync"/>. Discriminated record
/// (FluentResults-free) compatible with future transport boundaries.
/// </summary>
public sealed record SigningRequestOutcome
{
    public bool IsSuccess { get; init; }

    /// <summary>Durable public id of the created signing request. Used to poll status.</summary>
    public Guid RequestPublicId { get; init; }

    /// <summary>
    /// Strategy client hand-off relayed verbatim to the front end. For DocFlow: contains
    /// "handlerUrl", "jwt", and "docId". Empty on failure.
    /// </summary>
    public IReadOnlyDictionary<string, string> ClientHandoff { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Typed error code mapping to HTTP status (e.g. "SIGNING_ROUTE_UNSUPPORTED" → 400).</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable failure message. Never exposed to end users; for server logs / problem details.</summary>
    public string? ErrorMessage { get; init; }

    public static SigningRequestOutcome Success(Guid publicId, IReadOnlyDictionary<string, string> handoff) =>
        new() { IsSuccess = true, RequestPublicId = publicId, ClientHandoff = handoff };

    public static SigningRequestOutcome Fail(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}
