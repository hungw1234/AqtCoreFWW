using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Aggregate that tracks a multi-signer document-signing gate for one BusinessProcess transition step.
/// Each signer gets an ordered <see cref="DocumentSignatureSessionSigner"/> slot; the gate is
/// satisfied when at least <see cref="Threshold"/> slots reach <see cref="SignerSlotState.Completed"/>
/// with no slot stuck in <see cref="SignerSlotState.PendingVerify"/> or
/// <see cref="SignerSlotState.Rejected"/>.
///
/// <para>Sequential ordering (signer N must wait for signer N-1 to complete) is enforced by
/// <c>SignatureOrchestrator.EnforceSequenceGuardAsync</c>, not by this aggregate. The aggregate
/// state-machine accepts slot mutations in any order; ordering is a caller concern.</para>
///
/// <para>Ownership: Identity module (owns signing). BusinessProcess reaches this only via
/// <c>IDocumentSignatureSessionClient</c> — never via a direct assembly reference.</para>
///
/// <para>T2 entity: long Id (internal) + Guid PublicId (external caller reference).
/// Schema [identity] via IdentityDbContext default schema. Tenant-scoped.</para>
/// </summary>
public sealed class DocumentSignatureSession : AuditableEntity, ITenantScoped, IPublicIdentified
{
    /// <inheritdoc/>
    public Guid PublicId { get; private set; }

    /// <inheritdoc/>
    public Guid TenantId { get; private set; }

    /// <summary>BusinessProcess instance that owns this gate (cross-module reference by id only).</summary>
    public Guid BpInstanceId { get; private set; }

    /// <summary>
    /// Stable key identifying the transition step inside the BP (e.g. transition id or a caller-chosen
    /// slug). Combined with <see cref="BpInstanceId"/> uniquely identifies one signing gate.
    /// </summary>
    public string StepKey { get; private set; } = string.Empty;

    /// <summary>
    /// Reference to the artifact being signed (storage key for an uploaded PDF, or a form-submission id
    /// for a generated PDF). Threaded into each <see cref="SigningRequest"/> via the orchestrator.
    /// </summary>
    public string ArtifactRef { get; private set; } = string.Empty;

    /// <summary>
    /// Minimum number of signer slots that must reach Completed to satisfy the gate (M-of-N).
    /// Default equals the total signer count (all-of-N).
    /// </summary>
    public int Threshold { get; private set; }

    // Navigation property — EF populates via this List<>; never null after db materialisation.
    // Exposed as IReadOnlyList<> to callers so mutation must go through the aggregate methods.
    private List<DocumentSignatureSessionSigner> _signers = [];

    /// <summary>Ordered signer slots for this session (child rows, sequence 1-based).</summary>
    public IReadOnlyList<DocumentSignatureSessionSigner> Signers => _signers.AsReadOnly();

    /// <summary>EF Core: private parameterless constructor for materialisation.</summary>
    private DocumentSignatureSession() { }

    /// <summary>
    /// Factory — creates a new session with the ordered signer list. Validates that
    /// <paramref name="orderedSigners"/> is non-empty and <paramref name="threshold"/> is in range.
    /// </summary>
    public static DocumentSignatureSession Create(
        IGuidGenerator guidGenerator,
        Guid tenantId,
        Guid bpInstanceId,
        string stepKey,
        string artifactRef,
        IReadOnlyList<Guid> orderedSigners,
        int threshold,
        Guid createdBy)
    {
        ArgumentNullException.ThrowIfNull(guidGenerator);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (bpInstanceId == Guid.Empty)
            throw new ArgumentException("BpInstanceId must not be empty.", nameof(bpInstanceId));
        if (string.IsNullOrWhiteSpace(stepKey))
            throw new ArgumentException("StepKey must not be empty.", nameof(stepKey));
        if (string.IsNullOrWhiteSpace(artifactRef))
            throw new ArgumentException("ArtifactRef must not be empty.", nameof(artifactRef));
        if (orderedSigners is null || orderedSigners.Count == 0)
            throw new ArgumentException("At least one signer is required.", nameof(orderedSigners));
        if (threshold < 1 || threshold > orderedSigners.Count)
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                $"Threshold must be between 1 and {orderedSigners.Count} (the signer count).");
        if (createdBy == Guid.Empty)
            throw new ArgumentException("CreatedBy must not be empty.", nameof(createdBy));

        var session = new DocumentSignatureSession
        {
            PublicId     = guidGenerator.NewIdMonotonic(),
            TenantId     = tenantId,
            BpInstanceId = bpInstanceId,
            StepKey      = stepKey,
            ArtifactRef  = artifactRef,
            Threshold    = threshold,
        };
        session.SetAuditCreate(createdBy);

        for (var i = 0; i < orderedSigners.Count; i++)
        {
            var signer = DocumentSignatureSessionSigner.Create(
                tenantId, session, orderedSigners[i], sequenceNumber: i + 1, createdBy);
            session._signers.Add(signer);
        }

        return session;
    }

    // ─── Gate state machine ──────────────────────────────────────────────────

    /// <summary>
    /// Returns true when at least <see cref="Threshold"/> signer slots are Completed and no slot is
    /// PendingVerify. A PendingVerify slot blocks the gate even when the threshold count would
    /// otherwise be satisfied — the cert-bind verdict is required before the gate can advance.
    /// </summary>
    public bool IsGateSatisfied()
    {
        // Any PendingVerify slot blocks the gate unconditionally (locked decision #7).
        if (_signers.Any(s => s.State == SignerSlotState.PendingVerify))
            return false;

        var completedCount = _signers.Count(s => s.State == SignerSlotState.Completed);
        return completedCount >= Threshold;
    }

    /// <summary>
    /// Returns the next signer whose slot is still Pending in sequence order. Returns null when
    /// all slots are terminal (Completed or Rejected) or when the gate is already satisfied.
    /// </summary>
    public Guid? NextExpectedSigner()
    {
        return _signers
            .Where(s => s.State == SignerSlotState.Pending)
            .OrderBy(s => s.SequenceNumber)
            .Select(s => (Guid?)s.SignerId)
            .FirstOrDefault();
    }

    /// <summary>
    /// Marks the named signer's slot as Completed. Idempotent: no-op when already Completed.
    /// Throws when the signer is not in this session or the slot is in a terminal non-Completed state.
    ///
    /// <para>Sequential ordering enforcement (signer N must wait for N-1 to complete) is performed
    /// by the orchestrator's sequence guard — not enforced here in the aggregate.</para>
    /// </summary>
    public void MarkSignerCompleted(Guid signerId, Guid signingRequestPublicId, Guid modifiedBy)
    {
        var slot = GetSignerSlotOrThrow(signerId);
        if (slot.State == SignerSlotState.Completed) return; // idempotent

        if (slot.State == SignerSlotState.Rejected)
            throw new InvalidOperationException(
                $"Cannot mark signer {signerId} as Completed — slot is Rejected.");

        slot.MarkCompleted(signingRequestPublicId, modifiedBy);
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Marks the named signer's slot as PendingVerify (cert-bind awaiting Phase-6 verification).
    /// The gate stays unsatisfied while any slot is PendingVerify — the BP transition cannot advance.
    /// Idempotent: no-op when already PendingVerify.
    /// </summary>
    public void MarkSignerPendingVerify(Guid signerId, Guid signingRequestPublicId, Guid modifiedBy)
    {
        var slot = GetSignerSlotOrThrow(signerId);
        if (slot.State == SignerSlotState.PendingVerify) return; // idempotent

        if (slot.State is SignerSlotState.Completed or SignerSlotState.Rejected)
            throw new InvalidOperationException(
                $"Cannot transition signer {signerId} from {slot.State} to PendingVerify.");

        slot.MarkPendingVerify(signingRequestPublicId, modifiedBy);
        SetAuditUpdate(modifiedBy);
    }

    private DocumentSignatureSessionSigner GetSignerSlotOrThrow(Guid signerId)
        => _signers.FirstOrDefault(s => s.SignerId == signerId)
           ?? throw new InvalidOperationException(
               $"Signer {signerId} is not a participant in session {PublicId}.");
}

/// <summary>
/// One signer's ordered slot within a <see cref="DocumentSignatureSession"/>. Child entity — no
/// independent lifecycle; EF maps this as a HasMany owned navigation with a separate table.
/// </summary>
public sealed class DocumentSignatureSessionSigner : AuditableEntity, ITenantScoped
{
    /// <inheritdoc/>
    public Guid TenantId { get; private set; }

    /// <summary>FK to the owning <see cref="DocumentSignatureSession"/> (long PK).</summary>
    public long SessionId { get; private set; }

    /// <summary>Identity of the assigned signer (FK to AspNetUsers.Id).</summary>
    public Guid SignerId { get; private set; }

    /// <summary>1-based position in the ordered signing chain. Signer at sequence N may not begin
    /// until sequence N-1 is Completed.</summary>
    public int SequenceNumber { get; private set; }

    /// <summary>Current lifecycle state of this signer's slot.</summary>
    public SignerSlotState State { get; private set; } = SignerSlotState.Pending;

    /// <summary>
    /// Public id of the <see cref="SigningRequest"/> created when this signer's slot was initiated.
    /// Null until the signing request is dispatched (slot advances from the queue).
    /// </summary>
    public Guid? SigningRequestPublicId { get; private set; }

    /// <summary>EF Core: private parameterless constructor for materialisation.</summary>
    private DocumentSignatureSessionSigner() { }

    internal static DocumentSignatureSessionSigner Create(
        Guid tenantId,
        DocumentSignatureSession session,
        Guid signerId,
        int sequenceNumber,
        Guid createdBy)
    {
        if (signerId == Guid.Empty)
            throw new ArgumentException("SignerId must not be empty.", nameof(signerId));
        if (sequenceNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "SequenceNumber must be >= 1.");

        var slot = new DocumentSignatureSessionSigner
        {
            TenantId       = tenantId,
            SessionId      = session.Id,
            SignerId        = signerId,
            SequenceNumber = sequenceNumber,
            State          = SignerSlotState.Pending,
        };
        slot.SetAuditCreate(createdBy);
        return slot;
    }

    internal void MarkCompleted(Guid signingRequestPublicId, Guid modifiedBy)
    {
        State = SignerSlotState.Completed;
        SigningRequestPublicId = signingRequestPublicId;
        SetAuditUpdate(modifiedBy);
    }

    internal void MarkPendingVerify(Guid signingRequestPublicId, Guid modifiedBy)
    {
        State = SignerSlotState.PendingVerify;
        SigningRequestPublicId = signingRequestPublicId;
        SetAuditUpdate(modifiedBy);
    }
}

/// <summary>
/// Lifecycle state of one signer's slot in a <see cref="DocumentSignatureSession"/>.
/// Mirrors <c>SignerSlotState</c> from the contract DTO (no cross-boundary type reuse).
/// </summary>
public enum SignerSlotState
{
    Pending       = 0,
    PendingVerify = 1,
    Completed     = 2,
    Rejected      = 3,
}
