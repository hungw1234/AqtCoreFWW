namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for workflow operations.
/// Monolith: InProcessWorkflowModuleClient (delegates to IMediator + repositories).
/// Microservice: gRPC client when Workflow module extracted.
/// Contract is FluentResults-free by design — outcomes use plain C# types compatible
/// with future gRPC / JSON transport.
/// </summary>
public interface IWorkflowModuleClient
{
    /// <summary>
    /// Create a new workflow instance for a business entity. Phase 5b: also stages the first
    /// <c>WorkflowTask</c> for the initial state (non-final case) within the same SaveChanges
    /// — atomic with the instance row (S7 single-SaveChanges invariant). On <c>WF_NO_HANDLER</c>
    /// (empty pool at initial state) the outcome carries <see cref="WorkflowInstanceResult.IsSuccess"/>
    /// = false and <see cref="WorkflowInstanceResult.ErrorCode"/> = <see cref="WorkflowErrorCodes.NoHandlerResolved"/>;
    /// no instance row is persisted. Throws <see cref="InvalidOperationException"/> when the
    /// definition does not exist (caller misuse).
    /// </summary>
    Task<WorkflowInstanceResult> CreateInstanceAsync(
        CreateWorkflowInstanceRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// P4 Slice 2C: create an instance from the WorkflowDefinition internal long FK.
    /// BusinessProcess stores WorkflowDefinitionId as bigint (T2 internal PK), not PublicId — this avoids a
    /// round-trip resolve. Mirrors Slice 2B precedent (IFormTemplateQueryClient.GetVersionSchemaByInternalIdAsync).
    /// Throws InvalidOperationException when the definition does not exist. Phase 5b: failure outcomes
    /// (<c>WF_NO_HANDLER</c>) surface via <see cref="WorkflowInstanceResult.IsSuccess"/> = false.
    /// </summary>
    Task<WorkflowInstanceResult> CreateInstanceByInternalIdAsync(
        long workflowDefinitionId,
        string entityType,
        Guid entityId,
        Guid tenantId,
        Guid createdBy,
        Guid? dossierId = null,
        CancellationToken ct = default);

    /// <summary>
    /// P4 Slice 2C: resolves (WorkflowInstance.PublicId, ActionName) → (TransitionId long, FromStateId long).
    /// Required by BP-side transition handler so it can look up the per-transition form map BEFORE
    /// calling ExecuteTransitionAsync (R8 form-before-exec ordering). Returns null when the instance does
    /// not exist OR is cross-tenant OR no transition matches (FromState, ActionName) — caller maps null
    /// to NotFound without leaking existence.
    /// </summary>
    Task<TransitionLookup?> ResolveTransitionIdAsync(
        Guid instanceId,
        string actionName,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Phase-03: create an instance by resolving the latest definition version for a key
    /// (e.g. "CaseProcess"). Callers avoid holding raw Guid DefinitionId.
    /// Throws InvalidOperationException if the definition key does not exist for the tenant.
    /// Phase 5b: delegates to <see cref="CreateInstanceAsync"/> — inherits <c>WF_NO_HANDLER</c>
    /// Fail outcome (<see cref="WorkflowInstanceResult.IsSuccess"/> = false) for empty-pool
    /// initial states. Callers MUST check <see cref="WorkflowInstanceResult.IsSuccess"/>.
    /// </summary>
    Task<WorkflowInstanceResult> CreateInstanceByDefinitionKeyAsync(
        string definitionKey,
        string entityType,
        Guid entityId,
        Guid tenantId,
        Guid createdBy,
        CancellationToken ct = default);

    /// <summary>
    /// Phase-03: resolve the active (non-completed) WorkflowInstance ID for a business entity.
    /// Returns null if no active instance exists OR the instance does not belong to the tenant
    /// (does not leak existence across tenants).
    /// If multiple active instances exist (data-integrity bug — unique index deferred to Phase-05),
    /// picks deterministically by StartedAt ASC and logs a warning.
    /// </summary>
    Task<Guid?> TryResolveInstanceForEntityAsync(
        string entityType,
        Guid entityId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a workflow transition on the given instance.
    /// Phase-02: delegates to the existing ExecuteTransitionCommandHandler via IMediator —
    /// zero duplicate transition logic (parallel-branch fan-out + ConditionsJson preserved).
    /// Error codes map to WorkflowErrorCodes.GOV_WFL_NNN constants (see ExecuteTransitionOutcome.ErrorCode).
    /// </summary>
    Task<ExecuteTransitionOutcome> ExecuteTransitionAsync(
        ExecuteTransitionRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// P2 (BP-Run R14): returns the tenant that owns the given WorkflowInstance, or null if
    /// the instance does not exist. Used by IFormSubmissionRunClient to enforce cross-tenant
    /// guard before creating a FormSubmission against a BP Run.
    /// Caller compares against expected tenant and rejects on mismatch.
    /// </summary>
    Task<Guid?> GetTenantForInstanceAsync(
        Guid instanceId,
        CancellationToken ct = default);

    /// <summary>
    /// BP MVP P7: resolves WorkflowDefinition PublicId → internal bigint Id, scoped to tenant.
    /// Returns null when the definition does not exist OR belongs to a different tenant
    /// (does not leak existence). Used by CreateBusinessProcess to accept FE-supplied Guid
    /// while persisting the bigint FK (BP.WorkflowDefinitionId is T2 long PK reference).
    /// </summary>
    Task<long?> TryResolveDefinitionInternalIdAsync(
        Guid definitionPublicId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// BP MVP P7-FU1 (Phase B): returns the valid state and transition long IDs for a WorkflowDefinition,
    /// scoped to tenant. Used by ReplaceFormMapsCommandHandler to validate that submitted
    /// WorkflowStateId / WorkflowTransitionId longs belong to this BP's definition (security gate).
    /// Returns null when the definition does not exist OR belongs to a different tenant (no existence leak).
    /// WorkflowState and WorkflowTransition are T1 entities (no Guid AK) — keyed by long Id only.
    /// </summary>
    Task<WorkflowDefinitionStructure?> GetDefinitionStructureAsync(
        long workflowDefinitionId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// BP MVP P7-FU1.2: inverse of <see cref="TryResolveDefinitionInternalIdAsync"/> —
    /// resolves WorkflowDefinition internal bigint Id → Guid PublicId, tenant-scoped.
    /// Surfaced to <c>BusinessProcessDetailDto</c> so the FE form-map designer can call the
    /// existing <c>GET /api/v1/workflow/definitions/{id:guid}</c> endpoint for picker options
    /// (state/transition display names) without leaking long bigint IDs in URLs.
    /// Returns null for not-found OR cross-tenant (no existence leak).
    /// </summary>
    Task<Guid?> TryGetDefinitionPublicIdAsync(
        long workflowDefinitionInternalId,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Phase 5c: runs design-time R1-R4 graph validation on a WorkflowDefinition
    /// (identified by its internal bigint Id, which is what BusinessProcess stores as FK).
    /// Returns an outcome with IsValid=true when the graph passes all rules;
    /// on failure, Violations lists per-rule details for caller error surfacing.
    /// Returns IsValid=false with a single "not found" violation when the definition does not exist.
    /// Architecture: BP module MUST use this client method — never reference Workflow.Application directly.
    /// </summary>
    Task<WorkflowGraphValidationOutcome> ValidateDefinitionGraphAsync(
        long workflowDefinitionId,
        CancellationToken ct = default);
}

/// <summary>
/// Valid state and transition long IDs for a WorkflowDefinition.
/// Used by ReplaceFormMaps to validate submitted state/transition IDs belong to the correct definition.
/// </summary>
public sealed record WorkflowDefinitionStructure(
    IReadOnlySet<long> StateIds,
    IReadOnlySet<long> TransitionIds);

public record CreateWorkflowInstanceRequest(
    Guid DefinitionId,
    string EntityType,
    Guid EntityId,
    Guid TenantId,
    Guid CreatedBy,
    // Optional per-run identity. Only the BusinessProcess-run path supplies a non-null value
    // (a fresh Guid v7) so its rows are exempt from the unique active-entity index → N concurrent
    // runs per definition. Non-BP callers leave it null → rows stay indexed (one-active-per-entity).
    Guid? DossierId = null);

/// <remarks>
/// <para><c>InstanceId</c>, <c>InitialStateId</c>, <c>InitialStateInternalId</c> are populated on
/// success only (<see cref="IsSuccess"/> = true). On failure all three are <see cref="Guid.Empty"/> / 0.</para>
/// <para><c>InitialStateId</c> remains <see cref="Guid"/> for contract stability but is documented vestigial
/// (WorkflowState is T1 — no Guid AK; <c>InProcessWorkflowModuleClient</c> returns <see cref="Guid.Empty"/>).
/// Phase-4c cleanup will drop the field; until then, do not rely on it.</para>
/// <para><c>InitialStateInternalId</c> added Slice 2C: BusinessProcess needs the long PK to look up
/// state form bindings via <c>GetStateFormTemplateIdsAsync</c>.</para>
/// <para><b>Phase 5b:</b> extended to a discriminated outcome (<see cref="IsSuccess"/> +
/// <see cref="ErrorCode"/> + <see cref="ErrorMessage"/>) so <c>CreateInstanceAsync</c> can surface
/// <c>WF_NO_HANDLER</c> (empty pool at initial state) without throwing. Callers MUST check
/// <see cref="IsSuccess"/> before consuming <see cref="InstanceId"/>. Definition-not-found still
/// throws <see cref="InvalidOperationException"/> (misuse, not a runtime user error).</para>
/// </remarks>
public record WorkflowInstanceResult(
    Guid InstanceId,
    Guid InitialStateId,
    long InitialStateInternalId)
{
    /// <summary>True for a successfully created instance; false when creation was rejected
    /// (e.g. <c>WF_NO_HANDLER</c> empty-pool at initial state).</summary>
    public bool IsSuccess { get; init; } = true;

    /// <summary>GOV_WFL_NNN error code when <see cref="IsSuccess"/> is false; null on success.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable diagnostic message when <see cref="IsSuccess"/> is false; null on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Factory for a failure outcome — all positional fields zeroed; caller maps
    /// <see cref="ErrorCode"/> to its own error surface.</summary>
    public static WorkflowInstanceResult Fail(string errorCode, string errorMessage) =>
        new(Guid.Empty, Guid.Empty, 0)
        {
            IsSuccess = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// Request envelope for a transition — ActionName-based (not TransitionId) so callers
/// that hold only business-level semantics (e.g. "Submit") do not need to pre-resolve IDs.
/// The client resolves (FromState, ActionName) → TransitionId internally.
/// </summary>
public sealed record ExecuteTransitionRequest(
    Guid InstanceId,
    string ActionName,
    Guid ActorId,
    Guid TenantId,
    string? Comment = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    IReadOnlyList<Guid>? FileIds = null,
    // Slice 10a DDTO-DEFAULT: optional assignor override threaded from BP path → handler.
    Guid? NextHandlerUserId = null);

/// <summary>Success payload: new state + finality flag for quick caller branching.</summary>
/// <remarks>
/// <para><c>NewStateId</c> remains <see cref="Guid"/> for contract stability but is documented vestigial
/// (WorkflowState is T1 — no Guid AK; <c>InProcessWorkflowModuleClient</c> returns <see cref="Guid.Empty"/>).
/// Phase-4c cleanup will drop the field; until then, do not rely on it.</para>
/// <para><c>NewStateInternalId</c> added Slice 2C: BusinessProcess needs the long PK to look up
/// next-state form bindings via <c>GetStateFormTemplateIdsAsync</c>.</para>
/// </remarks>
public sealed record ExecuteTransitionResult(
    Guid NewStateId,
    string NewStateName,
    bool IsFinal,
    long NewStateInternalId);

/// <summary>
/// P4 Slice 2C: outcome of <see cref="IWorkflowModuleClient.ResolveTransitionIdAsync"/>.
/// FromStateId is the state the instance was in when the transition was resolved — used as
/// the scope for any transition form submitted alongside the transition execution.
/// </summary>
public sealed record TransitionLookup(
    long TransitionId,
    long FromStateId);

/// <summary>
/// Discriminated-union-ish outcome (IsSuccess flag + mutually exclusive payloads).
/// Record with public init-only props + factory methods — JSON/gRPC-serializable by default.
/// </summary>
public sealed record ExecuteTransitionOutcome
{
    public bool IsSuccess { get; init; }
    public ExecuteTransitionResult? Result { get; init; }

    /// <summary>GOV_WFL_NNN code — see WorkflowErrorCodes for the full list.</summary>
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static ExecuteTransitionOutcome Success(ExecuteTransitionResult result) =>
        new() { IsSuccess = true, Result = result };

    public static ExecuteTransitionOutcome Fail(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

/// <summary>
/// One entry in the actor's allowed-transition list.
/// ActionName drives ExecuteTransitionRequest; labels are i18n-ready for UI.
/// </summary>
public sealed record AllowedTransition(
    string ActionName,
    string ToStateName,
    string ActionLabelVi,
    string ActionLabelEn);

/// <summary>
/// Phase 5c: outcome of <see cref="IWorkflowModuleClient.ValidateDefinitionGraphAsync"/>.
/// Contract DTO — no FluentResults, no Workflow.Domain types (gRPC/JSON-serializable).
/// </summary>
public sealed record WorkflowGraphValidationOutcome
{
    public bool IsValid { get; init; }

    /// <summary>Per-rule violation details. Empty when IsValid=true.</summary>
    public IReadOnlyList<WorkflowGraphViolationDto> Violations { get; init; }
        = Array.Empty<WorkflowGraphViolationDto>();

    public static WorkflowGraphValidationOutcome Ok() =>
        new() { IsValid = true };

    public static WorkflowGraphValidationOutcome Fail(IReadOnlyList<WorkflowGraphViolationDto> violations) =>
        new() { IsValid = false, Violations = violations };
}

/// <summary>
/// One rule violation in a WorkflowGraphValidationOutcome.
/// Mirrors GraphRuleViolation from Workflow.Domain.Validation but is a contract DTO (no Domain dep).
/// </summary>
public sealed record WorkflowGraphViolationDto(
    string Rule,
    string? StateName,
    string? TransitionActionName,
    string Detail);
