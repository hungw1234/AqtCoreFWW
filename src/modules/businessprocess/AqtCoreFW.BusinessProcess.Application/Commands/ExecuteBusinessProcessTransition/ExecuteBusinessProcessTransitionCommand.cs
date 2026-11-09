using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentResults;
using MediatR;
using SignerSlotStatus = AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotStatus;

namespace AqtCoreFW.BusinessProcess.Application.Commands.ExecuteBusinessProcessTransition;

/// <summary>
/// P4 Slice 2C: executes a workflow transition on a BP-Run, bundling the transition form (if configured
/// and supplied) into the same ambient transaction. Discriminated outcome: <c>RequiresForms</c> when the
/// transition needs a form not yet submitted, <c>RequiresMoreSignatures</c> when a sign-gate is not yet
/// satisfied, otherwise <c>Succeeded</c> with new-state metadata.
/// </summary>
/// <remarks>
/// Intentionally NOT an <c>ICommand</c>/<c>IBaseCommand</c>, so the generic <c>AuditBehavior</c> does
/// not fire: the actual state change is delegated to <c>IWorkflowModuleClient.ExecuteTransitionAsync</c>
/// and recorded by the workflow-engine instance history — the canonical operational trail — making a
/// thin command-audit row redundant. This mirrors Workflow's sibling runtime commands (ExecuteTransition
/// /CreateWorkflowInstance), which also bypass IBaseCommand; only BP/Workflow definition-config commands
/// carry it. Tenant isolation is enforced by the handler's explicit cross-tenant guard, not the
/// IBaseCommand-gated TenantAwareBehavior.
/// </remarks>
public sealed record ExecuteBusinessProcessTransitionCommand(
    Guid WorkflowInstanceId,
    Guid BpPublicId,
    string ActionName,
    long? TransitionFormTemplateId,
    IReadOnlyDictionary<string, object?>? TransitionFormData,
    bool ConsentGiven,
    string? Comment,
    Guid TenantId,
    Guid ActorId,
    // Slice 10a DDTO-DEFAULT: optional assignor override threaded from BP HTTP → workflow handler.
    Guid? NextHandlerUserId = null,
    // Digital-signature proof bundle for a bundled transition form that carries a DigitalSignature
    // field; null otherwise. Verified at ingest against server-recomputed content (QĐ-742).
    BpRunSignaturePayload? Signature = null,
    // Sign-gate configuration: when non-null, the transition is gated on M-of-N signatures from
    // the ordered signer list before the workflow transition executes. The gate is resolved BEFORE
    // ExecuteTransitionAsync (mirrors the form-gate pattern). StepKey identifies the gate within
    // this BP instance — must be stable across repeated calls (e.g. the transition id as string).
    BpSignGateConfig? SignGateConfig = null)
    : IRequest<Result<ExecuteBusinessProcessTransitionOutcome>>;

/// <summary>
/// Configuration for a sign-gate on a BP transition. Supplied by the HTTP layer from the BP
/// definition's transition sign-map (analogous to <c>BpTransitionFormBinding</c> for forms).
/// <para>
/// <see cref="StepKey"/> is a stable identifier for the gate within the BP instance (e.g. the
/// transition long id as string). Combined with the WorkflowInstanceId it uniquely identifies
/// one signing session so the session is idempotently opened on every call.
/// </para>
/// </summary>
public sealed record BpSignGateConfig(
    /// <summary>Stable key for this gate within the BP instance (e.g. transition id as string).</summary>
    string StepKey,
    /// <summary>Reference to the artifact to be signed (storage key or form-submission id).</summary>
    string ArtifactRef,
    /// <summary>Ordered list of signer user ids (position in list = signing sequence).</summary>
    IReadOnlyList<Guid> OrderedSigners,
    /// <summary>M-of-N threshold. Null = all-of-N (default).</summary>
    int? Threshold = null);

/// <summary>
/// Discriminated-union-ish payload — JSON-serializable record with nullable branches.
/// <c>RequiresForms</c> = true when the caller must POST the transition form before re-attempting.
/// <c>RequiresSignatures</c> = true when the sign-gate is not yet satisfied; <c>NextSignerId</c>
/// carries the next expected signer so the FE can drive a signing ceremony.
/// <c>Success</c> = true on a completed transition (state advanced).
/// </summary>
public sealed record ExecuteBusinessProcessTransitionOutcome
{
    public bool RequiresForms { get; init; }
    public IReadOnlyList<BpRequiredTransitionForm>? TransitionForms { get; init; }

    /// <summary>
    /// True when a sign-gate is configured for this transition and the M-of-N threshold is not yet
    /// satisfied. <c>NextSignerId</c> is the next signer in sequence whose slot is still Pending.
    /// The FE drives a signing ceremony for that signer and re-triggers the transition when done.
    /// </summary>
    public bool RequiresSignatures { get; init; }

    /// <summary>
    /// The next signer the FE must collect a signature from. Populated only when
    /// <c>RequiresSignatures</c> is true. Guid.Empty when all signer slots are terminal but
    /// the threshold is still not met (e.g. a slot was Rejected — admin intervention required).
    /// </summary>
    public Guid? NextSignerId { get; init; }

    /// <summary>
    /// Point-in-time snapshot of all signer slots when <c>RequiresSignatures</c> is true.
    /// Allows the FE to show progress (who has signed, who is next, who is pending verify).
    /// </summary>
    public IReadOnlyList<SignerSlotStatus>? SignerSlots { get; init; }

    /// <summary>PublicId of the signing session. Null when no sign-gate applies.</summary>
    public Guid? SigningSessionId { get; init; }

    public bool Success { get; init; }
    public string? NewStateName { get; init; }
    public long? NewStateInternalId { get; init; }
    public bool? IsFinal { get; init; }
    public IReadOnlyList<long>? StateForms { get; init; }
    public Guid? TransitionFormSubmissionId { get; init; }

    public static ExecuteBusinessProcessTransitionOutcome RequireForms(IReadOnlyList<BpRequiredTransitionForm> forms) =>
        new() { RequiresForms = true, TransitionForms = forms };

    /// <summary>
    /// Gate not satisfied: the caller must drive the next signing ceremony for
    /// <paramref name="nextSignerId"/> then re-trigger the transition.
    /// </summary>
    public static ExecuteBusinessProcessTransitionOutcome RequiresMoreSignatures(
        Guid sessionId,
        Guid nextSignerId,
        IReadOnlyList<SignerSlotStatus> signerSlots) =>
        new()
        {
            RequiresSignatures = true,
            SigningSessionId   = sessionId,
            NextSignerId       = nextSignerId,
            SignerSlots        = signerSlots,
        };

    public static ExecuteBusinessProcessTransitionOutcome Succeeded(
        string newStateName,
        long newStateInternalId,
        bool isFinal,
        IReadOnlyList<long> stateForms,
        Guid? transitionFormSubmissionId) =>
        new()
        {
            Success = true,
            NewStateName = newStateName,
            NewStateInternalId = newStateInternalId,
            IsFinal = isFinal,
            StateForms = stateForms,
            TransitionFormSubmissionId = transitionFormSubmissionId
        };
}

/// <summary>
/// A transition form the caller must fill before the transition can proceed. Carries the published
/// version-snapshot (<c>SchemaJson</c>) inline so the FE can render fields without a follow-up fetch —
/// mirrors the intake-form / state-form snapshot contract.
/// </summary>
public sealed record BpRequiredTransitionForm(
    long TransitionFormTemplateId,
    Guid FormTemplatePublicId,
    string SchemaJson);
