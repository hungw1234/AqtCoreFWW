namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module façade: allows BusinessProcess (and any other module) to open, query, and advance a
/// multi-signer document-signing session without referencing Identity.Application or Identity.Infrastructure.
///
/// Monolith: <c>InProcessDocumentSignatureSessionClient</c> in Identity.Infrastructure.
/// Microservice: gRPC client when Identity is extracted.
///
/// Contract is FluentResults-free by design — outcomes use plain C# discriminated-record types compatible
/// with future gRPC / JSON transport. Mirrors <see cref="IWorkflowModuleClient"/> shape.
/// All tenant-scope assertions are explicit (TenantId param on every call) — do NOT rely on RLS, which is
/// bypassed under db_owner in the integration test connection.
/// </summary>
public interface IDocumentSignatureSessionClient
{
    /// <summary>
    /// Opens (or idempotently returns) a multi-signer signing session for a specific BusinessProcess
    /// transition step. When a session already exists for <paramref name="bpInstanceId"/> +
    /// <paramref name="stepKey"/>, the existing session is returned rather than creating a duplicate.
    ///
    /// <para>
    /// <paramref name="orderedSigners"/> is the ordered list of signers (position = 1-based sequence).
    /// <paramref name="threshold"/> is the minimum number of completions required to satisfy the gate
    /// (M-of-N, default = N = all). The server ignores any caller-supplied completion claim — gate state
    /// is always re-computed from the stored signer records.
    /// </para>
    /// </summary>
    Task<OpenSessionOutcome> OpenSessionAsync(
        Guid tenantId,
        Guid bpInstanceId,
        string stepKey,
        string artifactRef,
        IReadOnlyList<Guid> orderedSigners,
        int? threshold,
        CancellationToken ct = default);

    /// <summary>
    /// Queries the current gate state for an existing session: satisfied/not-satisfied, and when not
    /// satisfied, the next expected signer (the first signer in the ordered list whose slot is still
    /// Pending). Returns a typed failure outcome when the session does not exist or is cross-tenant.
    /// </summary>
    Task<SessionGateStateOutcome> GetGateStateAsync(
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a signer's slot as completed (called by the orchestrator after a signing request
    /// reaches terminal-Completed status). Idempotent: re-marking an already-Completed slot is a no-op.
    /// Returns a typed failure when the session or signer slot is not found / cross-tenant.
    /// </summary>
    Task<SignerSlotUpdateOutcome> MarkSignerCompletedAsync(
        Guid sessionId,
        Guid tenantId,
        Guid signerId,
        Guid signingRequestPublicId,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a signer's slot as PendingVerify (post-upload cert-bind pending confirmation).
    /// The gate is NOT satisfied while any slot is PendingVerify — the transition cannot advance.
    /// Returns a typed failure when the session or signer slot is not found / cross-tenant.
    /// </summary>
    Task<SignerSlotUpdateOutcome> MarkSignerPendingVerifyAsync(
        Guid sessionId,
        Guid tenantId,
        Guid signerId,
        Guid signingRequestPublicId,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the next expected signer for a session (the first Pending slot in sequence order).
    /// Returns null inside the outcome when all slots are terminal (Completed or Rejected) — callers
    /// should re-check gate state at that point. Returns a typed failure for not-found / cross-tenant.
    /// </summary>
    Task<NextSignerOutcome> GetNextSignerAsync(
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default);
}

// ─── Outcome types (discriminated records, FluentResults-free) ────────────────

/// <summary>Outcome of <see cref="IDocumentSignatureSessionClient.OpenSessionAsync"/>.</summary>
public sealed record OpenSessionOutcome
{
    public bool IsSuccess { get; init; }
    public Guid SessionId { get; init; }
    public bool WasExisting { get; init; }

    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static OpenSessionOutcome Created(Guid sessionId) =>
        new() { IsSuccess = true, SessionId = sessionId, WasExisting = false };

    public static OpenSessionOutcome Existing(Guid sessionId) =>
        new() { IsSuccess = true, SessionId = sessionId, WasExisting = true };

    public static OpenSessionOutcome Fail(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

/// <summary>Outcome of <see cref="IDocumentSignatureSessionClient.GetGateStateAsync"/>.</summary>
public sealed record SessionGateStateOutcome
{
    public bool IsSuccess { get; init; }

    /// <summary>True when the M-of-N threshold is satisfied and the BP transition can advance.</summary>
    public bool GateSatisfied { get; init; }

    /// <summary>
    /// The next signer whose slot is Pending in sequence order. Null when the gate is satisfied
    /// or all active slots have reached a terminal state.
    /// </summary>
    public Guid? NextExpectedSigner { get; init; }

    /// <summary>
    /// Per-signer slot status snapshot (for notification / FE display). Never null on success.
    /// </summary>
    public IReadOnlyList<SignerSlotStatus> SignerSlots { get; init; } = Array.Empty<SignerSlotStatus>();

    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static SessionGateStateOutcome Satisfied(IReadOnlyList<SignerSlotStatus> slots) =>
        new() { IsSuccess = true, GateSatisfied = true, SignerSlots = slots };

    public static SessionGateStateOutcome Pending(Guid nextSigner, IReadOnlyList<SignerSlotStatus> slots) =>
        new() { IsSuccess = true, GateSatisfied = false, NextExpectedSigner = nextSigner, SignerSlots = slots };

    public static SessionGateStateOutcome Fail(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

/// <summary>Outcome of signer-slot mutation methods.</summary>
public sealed record SignerSlotUpdateOutcome
{
    public bool IsSuccess { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static SignerSlotUpdateOutcome Ok() => new() { IsSuccess = true };
    public static SignerSlotUpdateOutcome Fail(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

/// <summary>Outcome of <see cref="IDocumentSignatureSessionClient.GetNextSignerAsync"/>.</summary>
public sealed record NextSignerOutcome
{
    public bool IsSuccess { get; init; }

    /// <summary>Next Pending signer in sequence order. Null when all slots are terminal.</summary>
    public Guid? NextSignerId { get; init; }

    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static NextSignerOutcome Found(Guid signerId) =>
        new() { IsSuccess = true, NextSignerId = signerId };

    public static NextSignerOutcome AllTerminal() =>
        new() { IsSuccess = true, NextSignerId = null };

    public static NextSignerOutcome Fail(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

/// <summary>Point-in-time status of one signer's slot within a session.</summary>
public sealed record SignerSlotStatus(
    Guid SignerId,
    int SequenceNumber,
    SignerSlotState State,
    Guid? SigningRequestPublicId);

/// <summary>Lifecycle state of one signer's slot in a multi-signer session.</summary>
public enum SignerSlotState
{
    /// <summary>Signer has not yet started. The slot is Pending if it is the next in sequence,
    /// or if all prior slots are Completed (strict sequence ordering).</summary>
    Pending = 0,

    /// <summary>Post-upload cert-bind is awaiting Phase-6 verification. Gate stays unsatisfied
    /// while any slot is PendingVerify — the BP transition cannot advance.</summary>
    PendingVerify = 1,

    /// <summary>Signer has completed; the signed artifact is available for the next signer.</summary>
    Completed = 2,

    /// <summary>Signer's cert-bind was rejected (Phase-6 verification failure) or the signer
    /// actively rejected the document. Evidence retained; gate stays unsatisfied.</summary>
    Rejected = 3,
}
