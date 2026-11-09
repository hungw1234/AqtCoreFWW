namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// GOV error codes for Workflow module — GOV_WFL_NNN convention.
/// Lives in SharedKernel.Contracts so cross-module consumers (e.g. Cases handlers mapping
/// ExecuteTransitionOutcome.ErrorCode → HTTP status) don't need to reference Workflow.Domain.
/// Reserved range GOV_WFL_100–109: future cross-module client errors.
/// </summary>
public static class WorkflowErrorCodes
{
    public const string DefinitionNotFound     = "GOV_WFL_001";
    public const string InstanceNotFound       = "GOV_WFL_002";
    public const string InvalidTransition      = "GOV_WFL_003";
    public const string TransitionForbidden    = "GOV_WFL_004";
    public const string NoInitialState         = "GOV_WFL_005";
    public const string DuplicateStateName     = "GOV_WFL_006";
    public const string DuplicateTransition    = "GOV_WFL_007";
    public const string DefinitionHasInstances = "GOV_WFL_008";
    public const string StateNotInDefinition   = "GOV_WFL_009";
    public const string SlaConfigNotFound      = "GOV_WFL_010";
    public const string DefinitionNotLatest    = "GOV_WFL_011";
    public const string ConditionNotMet        = "GOV_WFL_012";

    /// <summary>
    /// Raised when a simultaneous transition race is detected via EF optimistic concurrency
    /// token on <c>WorkflowInstance.CurrentStateId</c>. Distinct from <see cref="InvalidTransition"/>
    /// (which catches sequential races early at app-layer before DB round-trip).
    /// Maps to HTTP 409.
    /// </summary>
    public const string StateConflict          = "GOV_WFL_013";

    /// <summary>
    /// Raised when the Model B handler resolver returns an empty pool for a non-final target state.
    /// Indicates workflow misconfiguration (no assignment rules cover the outgoing transitions).
    /// Maps to HTTP 422 (ValidationError — workflow config problem, not a caller error).
    /// </summary>
    public const string NoHandlerResolved      = "GOV_WFL_014";

    /// <summary>
    /// Raised when parallel branch fan-out staging fails (e.g. unique-index violation on
    /// WorkflowInstanceBranchStatus or internal branch staging error). The entire transition
    /// is rolled back atomically. Maps to HTTP 422 (ValidationError — ops actionable).
    /// Slice 6: replaces silent swallow-catch (RT3-FM-F4 atomic guarantee).
    /// </summary>
    public const string BranchFanOutFailed     = "GOV_WFL_015";

    /// <summary>
    /// Raised when a pool task Claim attempt fails because the task is already claimed
    /// by another user (sequential guard: domain Claim() throws InvalidOperationException,
    /// or concurrent race: RowVersion mismatch → DbUpdateConcurrencyException).
    /// Both paths translate to this ConflictError. Maps to HTTP 409.
    /// Note: task spec mentioned "WF_ALREADY_CLAIMED" literal; project GOV_WFL_NNN
    /// convention is authoritative — orchestrator can override if literal compliance required.
    /// P4 Slice 4-B.
    /// </summary>
    public const string AlreadyClaimed         = "GOV_WFL_016";

    /// <summary>
    /// Raised when a Claim or Unclaim attempt targets a task already in a terminal state
    /// (Done or Cancelled — WorkflowTaskStatus enum values).
    /// Distinct from AlreadyClaimed (which means claimed-but-active).
    /// Maps to HTTP 409.
    /// P4 Slice 4-C: M1 exception-discrimination carry-forward from 4-B.
    /// </summary>
    public const string TaskFinalState         = "GOV_WFL_017";

    /// <summary>
    /// Raised when GetInboxQuery is invoked with a resolver mode not yet implemented
    /// (Role/Group/OrgUnit — deferred per RT2-15). MVP supports ByPermission only.
    /// Maps to HTTP 422 (ValidationError — ops-actionable config issue).
    /// P4 Slice 4-C.
    /// </summary>
    public const string NotImplementedMode     = "GOV_WFL_018";

    /// <summary>
    /// Raised when workflow graph validation fails at a publish surface
    /// (Save R1 check, ToggleActive R1-R4 check, BP Activate R1-R4 check).
    /// Indicates workflow misconfiguration (dead-end states, missing assignments,
    /// unknown permission codes, or NotSupported clause modes).
    /// Maps to HTTP 422 (ValidationError — ops-actionable).
    /// Phase 5c design-time validation.
    /// </summary>
    public const string GraphValidationFailed  = "GOV_WFL_019";

    /// <summary>
    /// Raised when a second active WorkflowInstance for the same
    /// (EntityType, EntityId, TenantId) collides with the unique active-entity index
    /// (SqlException 2601/2627). One-active-per-entity guard for flows that leave DossierId NULL
    /// (non-BusinessProcess); BusinessProcess runs carry a per-run DossierId and are exempt.
    /// Maps to HTTP 409.
    /// </summary>
    public const string InstanceAlreadyActive  = "GOV_WFL_020";
}
