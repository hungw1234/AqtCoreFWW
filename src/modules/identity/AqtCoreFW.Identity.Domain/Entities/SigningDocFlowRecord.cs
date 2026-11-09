using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Sign-type mapping to the 7 VGCA plugin functions (W3, QĐ 742 mục 5.1).
/// </summary>
public enum DocFlowSignType
{
    Issued    = 1, // vgca_sign_issued   — văn thư phát hành
    Approved  = 2, // vgca_sign_approved — lãnh đạo phê duyệt
    Income    = 3, // vgca_sign_income   — công văn đến
    Comment   = 4, // vgca_comment       — góp ý / phê bình
    Appendix  = 5, // vgca_sign_appendix — phụ lục đính kèm
    Copy      = 6, // vgca_sign_copy     — bản sao điện tử
    Files     = 7, // vgca_sign_files    — batch
}

/// <summary>
/// Lifecycle of a single doc-flow signing round-trip.
/// </summary>
public enum DocFlowStatus
{
    Initiated  = 0, // JWT issued; VGCA service not yet contacted
    Downloaded = 1, // VGCA service downloaded original PDF (nonce slot 1 consumed)
    Completed  = 2, // Signed PDF uploaded; saved to Files module (nonce slot 2 consumed)
    Failed     = 3, // Upload reported error or signature validation rejected
    Expired    = 4, // JWT TTL elapsed before VGCA completed the flow
}

/// <summary>
/// Tracks each Tier-3 doc-flow signing round-trip initiated via VGCA SignService.
///
/// <para>VGCA flow: FE POST /initiate → get {handlerUrl, jwt, docId} → call vgca_sign_*(params, cb)
/// → VGCA GETs handlerUrl (downloads original PDF) → signs with PIN → POSTs signed PDF back.
/// BE saves the uploaded PDF via IFileStorageService; this record tracks the state machine.</para>
///
/// <para>Schema: [identity] (via IdentityDbContext default schema).
/// PK: long Id (from AuditableEntity / Entity base — DB IDENTITY).
/// DocId: external document GUID passed by the FE.
/// JwtNonce: 1:1 FK to SigningDocFlowNonce.Nonce used for replay prevention.</para>
/// </summary>
public class SigningDocFlowRecord : AuditableEntity, ITenantScoped
{
    /// <inheritdoc/>
    public Guid TenantId { get; private set; }

    /// <summary>External document identifier supplied by the FE (references the doc to be signed).</summary>
    public Guid DocId { get; private set; }

    /// <summary>VGCA plugin function used for this signing operation.</summary>
    public DocFlowSignType SignType { get; private set; }

    /// <summary>Current lifecycle state of this doc-flow record.</summary>
    public DocFlowStatus Status { get; private set; }

    /// <summary>
    /// Storage key of the original (unsigned) PDF in the Files module bucket "signing-original".
    /// FE must supply this when calling /initiate — the BE has no doc registry.
    /// </summary>
    public string OriginalStorageKey { get; private set; } = string.Empty;

    /// <summary>
    /// Storage key of the signed PDF in the Files module bucket "signing-docflow" (null until Completed).
    /// Pattern: "{tenantId}/{docId}/{randomGuid}.signed.pdf".
    /// </summary>
    public string? SignedStorageKey { get; private set; }

    /// <summary>
    /// For incremental multi-signer PAdES chaining: the storage key (in bucket "signing-docflow") of
    /// the signed output produced by the preceding signer (sequence N-1). When sequence == 1, this is null
    /// and the desktop tool receives the original unsigned PDF. When sequence > 1, the desktop tool
    /// receives this artifact and appends its incremental signature without invalidating prior signatures.
    /// </summary>
    public string? PreviousSignedStorageKey { get; private set; }

    /// <summary>
    /// Position of this signer in an ordered multi-signer chain (1-based). Defaults to 1 for
    /// single-signer documents. Combined with (TenantId, DocId) it uniquely identifies a signing
    /// round in a chain and supports the prior-round Completed guard.
    /// </summary>
    public int SignerSequenceNumber { get; private set; } = 1;

    /// <summary>
    /// Nonce used to issue the short-lived JWT for this doc-flow session.
    /// 1:1 FK to SigningDocFlowNonce.Nonce (Guid PK); replay prevention.
    /// </summary>
    public Guid JwtNonce { get; private set; }

    /// <summary>User who initiated this signing session (FK to AspNetUsers.Id).</summary>
    public Guid InitiatedBy { get; private set; }

    /// <summary>UTC timestamp when /initiate was called.</summary>
    public DateTime InitiatedAt { get; private set; }

    /// <summary>UTC timestamp when the signed PDF was successfully uploaded (null until Completed).</summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>Human-readable failure reason when Status = Failed (null otherwise).</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Optimistic concurrency token — guards the status transitions (Downloaded/Completed) against
    /// concurrent callers. The in-memory <see cref="MarkDownloaded"/>/<see cref="Complete"/> pre-condition
    /// checks read the status loaded at this RowVersion; if a parallel request transitions the record
    /// first, the losing SaveChanges throws <c>DbUpdateConcurrencyException</c> instead of silently
    /// last-write-wins. EF maps this as a SQL Server rowversion column.
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>EF Core: private parameterless constructor for materialisation.</summary>
    private SigningDocFlowRecord() { }

    /// <summary>
    /// Factory — creates a new record in <see cref="DocFlowStatus.Initiated"/> state.
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="docId">External document GUID (passed by FE to the VGCA plugin).</param>
    /// <param name="signType">Which VGCA plugin function drives this round-trip.</param>
    /// <param name="originalStorageKey">Storage key of the unsigned PDF in bucket "signing-original".</param>
    /// <param name="jwtNonce">Nonce for the short-lived JWT; 1:1 FK to SigningDocFlowNonce.</param>
    /// <param name="initiatedBy">User who initiated this signing session.</param>
    /// <param name="signerSequenceNumber">
    /// Position in an ordered multi-signer chain (1-based, default 1). Combined with
    /// (TenantId, DocId) it uniquely identifies this round in the chain.
    /// </param>
    /// <param name="previousSignedStorageKey">
    /// Storage key (bucket "signing-docflow") of the signed PDF produced by signer N-1. Null for
    /// the first signer; required and validated for sequence &gt; 1 by the initiating command.
    /// </param>
    public static SigningDocFlowRecord Initiate(
        Guid tenantId,
        Guid docId,
        DocFlowSignType signType,
        string originalStorageKey,
        Guid jwtNonce,
        Guid initiatedBy,
        int signerSequenceNumber = 1,
        string? previousSignedStorageKey = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (docId == Guid.Empty)
            throw new ArgumentException("DocId must not be empty.", nameof(docId));
        if (string.IsNullOrWhiteSpace(originalStorageKey))
            throw new ArgumentException("OriginalStorageKey must not be empty.", nameof(originalStorageKey));
        if (jwtNonce == Guid.Empty)
            throw new ArgumentException("JwtNonce must not be empty.", nameof(jwtNonce));
        if (initiatedBy == Guid.Empty)
            throw new ArgumentException("InitiatedBy must not be empty.", nameof(initiatedBy));
        if (signerSequenceNumber < 1)
            throw new ArgumentException("SignerSequenceNumber must be >= 1.", nameof(signerSequenceNumber));

        var record = new SigningDocFlowRecord
        {
            TenantId                = tenantId,
            DocId                   = docId,
            SignType                = signType,
            Status                  = DocFlowStatus.Initiated,
            OriginalStorageKey      = originalStorageKey,
            JwtNonce                = jwtNonce,
            InitiatedBy             = initiatedBy,
            InitiatedAt             = DateTime.UtcNow,
            SignerSequenceNumber     = signerSequenceNumber,
            PreviousSignedStorageKey = previousSignedStorageKey,
        };
        record.SetAuditCreate(initiatedBy);
        return record;
    }

    /// <summary>
    /// Transitions to Downloaded — called when VGCA service GETs the original PDF.
    /// Uses protected Entity.MarkUpdated() — no userId needed (system state transition).
    /// </summary>
    public void MarkDownloaded()
    {
        if (Status != DocFlowStatus.Initiated)
            throw new InvalidOperationException(
                $"Cannot transition to Downloaded from {Status}.");
        Status = DocFlowStatus.Downloaded;
        SetAuditUpdate(InitiatedBy); // system transition; attribute to originator
    }

    /// <summary>Transitions to Completed — called when signed PDF has been saved to Files module.</summary>
    public void Complete(string signedStorageKey, Guid completedBy)
    {
        if (Status != DocFlowStatus.Downloaded)
            throw new InvalidOperationException(
                $"Cannot transition to Completed from {Status}.");
        if (string.IsNullOrWhiteSpace(signedStorageKey))
            throw new ArgumentException("SignedStorageKey must not be empty.", nameof(signedStorageKey));

        Status           = DocFlowStatus.Completed;
        SignedStorageKey = signedStorageKey;
        CompletedAt      = DateTime.UtcNow;
        SetAuditUpdate(completedBy);
    }

    /// <summary>Transitions to Failed — called when upload validation or storage fails.</summary>
    public void Fail(string reason, Guid failedBy)
    {
        if (Status is DocFlowStatus.Completed or DocFlowStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot transition to Failed from {Status}.");

        Status        = DocFlowStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown error" : reason;
        SetAuditUpdate(failedBy);
    }

    /// <summary>
    /// Marks as Expired — idempotent if already terminal.
    /// Called by background cleanup when JWT TTL has elapsed with no completion.
    /// </summary>
    public void Expire()
    {
        if (Status is DocFlowStatus.Completed or DocFlowStatus.Failed or DocFlowStatus.Expired)
            return;
        Status = DocFlowStatus.Expired;
        SetAuditUpdate(InitiatedBy); // system transition; attribute to originator
    }

    /// <summary>
    /// Soft-deletes this record so it leaves the filtered-active unique set and a fresh attempt
    /// at the same (TenantId, DocId, SignerSequenceNumber) can be inserted.
    ///
    /// Only valid for non-terminal records (Initiated or Downloaded). Terminal records (Completed,
    /// Failed, Expired) are already outside the filtered-unique set and must not be superseded —
    /// their history must be preserved for audit purposes.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called on a terminal record.</exception>
    public void Supersede(Guid supersededBy)
    {
        if (Status is DocFlowStatus.Completed or DocFlowStatus.Failed or DocFlowStatus.Expired)
            throw new InvalidOperationException(
                $"Cannot supersede a terminal record (Status={Status}). " +
                "Terminal records are already outside the active unique set.");

        MarkDeleted();
        SetAuditUpdate(supersededBy);
    }

    /// <summary>
    /// Returns true when this record is in a terminal state (Completed, Failed, or Expired)
    /// and therefore outside the filtered-active unique set.
    /// </summary>
    public bool IsTerminal =>
        Status is DocFlowStatus.Completed or DocFlowStatus.Failed or DocFlowStatus.Expired;
}
