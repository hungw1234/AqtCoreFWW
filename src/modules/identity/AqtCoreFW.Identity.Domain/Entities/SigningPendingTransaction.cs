using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Status of a Viettel Mysign async-callback pending transaction.
/// Transitions: Initiated → Completed | Failed | Expired (terminal states).
/// </summary>
public enum PendingTransactionStatus
{
    Initiated  = 0, // signHash submitted; waiting for Mysign callback
    Completed  = 1, // callback received; signature persisted
    Failed     = 2, // callback received with failure, or explicit fail
    Expired    = 3, // cleanup job expired after ExpiresAt + grace period
}

/// <summary>
/// Tracks a single Viettel Mysign async-callback (async=3) signing transaction.
///
/// <para>Lifecycle: SignAsync async=3 → <see cref="Initiate"/> → Mysign calls back BE →
/// <see cref="Complete"/> or <see cref="Fail"/> → FE polls /api/signing/confirm.</para>
///
/// <para>PK is <see cref="TransactionId"/> (string) — Viettel-assigned UUID; not a Guid
/// because Viettel may use string format with dashes, and we must do exact equality lookups.</para>
///
/// <para>Schema: [identity] (via IdentityDbContext default schema).</para>
/// </summary>
public sealed class SigningPendingTransaction : ITenantScoped
{
    /// <summary>Viettel-assigned transaction UUID returned by signHash async=3.</summary>
    public string TransactionId { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public Guid TenantId { get; private set; }

    /// <summary>UserId who initiated the signing — FK to AspNetUsers.Id (via string userId from Viettel).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Internal requestId from BE, for correlation with the signing command.</summary>
    public string RequestId { get; private set; } = string.Empty;

    /// <summary>Optional documentId passed to signHash; null if not applicable.</summary>
    public string? DocumentId { get; private set; }

    /// <summary>UTC timestamp when this transaction was created (signHash call completed).</summary>
    public DateTime InitiatedAt { get; private set; }

    /// <summary>UTC timestamp after which Mysign is not expected to call back. Used by cleanup job.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Current lifecycle status.</summary>
    public PendingTransactionStatus Status { get; private set; }

    /// <summary>Raw signature bytes received from Mysign callback (null until Completed).</summary>
    public byte[]? Signature { get; private set; }

    /// <summary>Human-readable failure reason when Status = Failed (null otherwise).</summary>
    public string? FailureReason { get; private set; }

    /// <summary>UTC timestamp when the callback was processed (null until terminal).</summary>
    public DateTime? CompletedAt { get; private set; }

    // ── PAdES async-poll resume state (option A) ──────────────────────────────────────────────────────────
    // Null for DocFlow / async-callback rows; populated only when this transaction backs a server-side embedded
    // PAdES signature, so the completion path can splice the CMS + store the signed PDF WITHOUT re-issuing
    // signHash (F1 idempotency — a polled signature is recoverable from this persisted state, never re-signed).

    /// <summary>Storage key of the staged placeholder PDF (ByteRange laid out, <c>/Contents</c> still zeros).</summary>
    public string? PadesPlaceholderStorageKey { get; private set; }

    /// <summary>Byte offset of the first <c>/Contents</c> hex digit — for the length-preserving CMS splice.</summary>
    public int? PadesContentsGapStart { get; private set; }

    /// <summary>Width (hex chars) of the <c>/Contents</c> placeholder gap.</summary>
    public int? PadesContentsHexLength { get; private set; }

    /// <summary>DER of the signed attributes — rebuilds the CMS together with the polled raw signature.</summary>
    public byte[]? PadesSignedAttributesDer { get; private set; }

    /// <summary>DER of the signer leaf cert — embedded in the CMS bag (and hashed into signing-certificate-v2).</summary>
    public byte[]? PadesSignerCertDer { get; private set; }

    /// <summary>Storage key of the final signed PDF, set when the completion path splices + stores it.</summary>
    public string? PadesSignedStorageKey { get; private set; }

    /// <summary>
    /// Optimistic concurrency token — prevents two concurrent callbacks processing the same transaction.
    /// EF Core maps this as a rowversion/timestamp column.
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>EF Core: private parameterless constructor for materialisation.</summary>
    private SigningPendingTransaction() { }

    /// <summary>
    /// Factory — creates a new transaction in <see cref="PendingTransactionStatus.Initiated"/> state.
    /// </summary>
    /// <param name="transactionId">Viettel-assigned transactionId from signHash response.</param>
    /// <param name="tenantId">Tenant who initiated signing.</param>
    /// <param name="userId">User who initiated signing.</param>
    /// <param name="requestId">Internal BE requestId for correlation.</param>
    /// <param name="documentId">Optional document identifier.</param>
    /// <param name="ttl">Time-to-live for this transaction (typically 24h).</param>
    public static SigningPendingTransaction Initiate(
        string transactionId,
        Guid tenantId,
        Guid userId,
        string requestId,
        string? documentId,
        TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            throw new ArgumentException("TransactionId must not be empty.", nameof(transactionId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("RequestId must not be empty.", nameof(requestId));
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("TTL must be positive.", nameof(ttl));

        var now = DateTime.UtcNow;
        return new SigningPendingTransaction
        {
            TransactionId = transactionId,
            TenantId      = tenantId,
            UserId        = userId,
            RequestId     = requestId,
            DocumentId    = documentId,
            InitiatedAt   = now,
            ExpiresAt     = now.Add(ttl),
            Status        = PendingTransactionStatus.Initiated,
        };
    }

    /// <summary>
    /// Transitions to Completed — called by callback result endpoint with signature bytes.
    /// </summary>
    public void Complete(byte[] signature)
    {
        if (Status != PendingTransactionStatus.Initiated)
            throw new InvalidOperationException(
                $"Cannot transition to Completed from {Status}.");

        if (signature is null || signature.Length == 0)
            throw new ArgumentException("Signature must not be empty.", nameof(signature));

        Status      = PendingTransactionStatus.Completed;
        Signature   = signature;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Attaches the PAdES resume state on an Initiated transaction so the async-poll completion path can finalize
    /// the signature without re-issuing signHash (F1). All parts are required — a partial state would make the
    /// completion path unable to assemble the CMS / splice the PDF.
    /// </summary>
    public void AttachPadesResumeState(
        string placeholderStorageKey,
        int contentsGapStart,
        int contentsHexLength,
        byte[] signedAttributesDer,
        byte[] signerCertDer)
    {
        if (Status != PendingTransactionStatus.Initiated)
            throw new InvalidOperationException($"Cannot attach PAdES resume state in {Status}.");
        // Once-only: re-attaching a DIFFERENT placeholder/signedAttrs after signHash was issued over the first
        // layout would make completion splice the signature into the wrong bytes (Adobe-invalid). Fail closed.
        if (PadesPlaceholderStorageKey is not null)
            throw new InvalidOperationException("PAdES resume state is already attached (attach is once-only per transaction).");
        if (string.IsNullOrWhiteSpace(placeholderStorageKey))
            throw new ArgumentException("Placeholder storage key must not be empty.", nameof(placeholderStorageKey));
        if (contentsGapStart <= 0)
            throw new ArgumentOutOfRangeException(nameof(contentsGapStart), "Must be a positive byte offset.");
        if (contentsHexLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(contentsHexLength), "Must be a positive width.");
        if (signedAttributesDer is null || signedAttributesDer.Length == 0)
            throw new ArgumentException("Signed attributes DER must not be empty.", nameof(signedAttributesDer));
        if (signerCertDer is null || signerCertDer.Length == 0)
            throw new ArgumentException("Signer cert DER must not be empty.", nameof(signerCertDer));

        PadesPlaceholderStorageKey = placeholderStorageKey;
        PadesContentsGapStart      = contentsGapStart;
        PadesContentsHexLength     = contentsHexLength;
        PadesSignedAttributesDer   = signedAttributesDer;
        PadesSignerCertDer         = signerCertDer;
    }

    /// <summary>
    /// Completes a PAdES transaction: stores the raw signature (reusing <see cref="Complete"/>'s state guard) and
    /// records the storage key of the assembled signed PDF the completion path produced.
    /// </summary>
    public void CompletePades(byte[] rawSignature, string signedPdfStorageKey)
    {
        // Validate BEFORE mutating: a blank key must fail closed with zero side effects, never leave the aggregate
        // Completed-but-keyless (which the F1 idempotency guard would treat as done with no retrievable PDF).
        if (string.IsNullOrWhiteSpace(signedPdfStorageKey))
            throw new ArgumentException("Signed PDF storage key must not be empty.", nameof(signedPdfStorageKey));
        Complete(rawSignature);
        PadesSignedStorageKey = signedPdfStorageKey;
    }

    /// <summary>
    /// Transitions to Failed — called by callback result endpoint on error, or on cleanup failure.
    /// </summary>
    public void Fail(string reason)
    {
        if (Status is PendingTransactionStatus.Completed or PendingTransactionStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot transition to Failed from {Status}.");

        Status        = PendingTransactionStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown failure" : reason;
        CompletedAt   = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks as Expired — idempotent if already terminal.
    /// Called by <see cref="AqtCoreFW.Identity.Infrastructure.BackgroundJobs.PendingTransactionCleanupJob"/>.
    /// </summary>
    public void MarkExpired()
    {
        // Idempotent on terminal states — cleanup job may re-scan.
        if (Status is PendingTransactionStatus.Completed
            or PendingTransactionStatus.Failed
            or PendingTransactionStatus.Expired)
            return;

        Status      = PendingTransactionStatus.Expired;
        CompletedAt = DateTime.UtcNow;
    }
}
