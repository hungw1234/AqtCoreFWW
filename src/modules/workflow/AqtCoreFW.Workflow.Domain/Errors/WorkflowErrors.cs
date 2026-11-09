using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Domain.Validation;
using FluentResults;

namespace AqtCoreFW.Workflow.Domain.Errors;

// WorkflowErrorCodes moved to AqtCoreFW.SharedKernel.Contracts.Clients so cross-module consumers
// (Cases handlers mapping ExecuteTransitionOutcome.ErrorCode → HTTP errors) don't need
// a reference on Workflow.Domain. Factory methods below delegate to the contract class.

public static class WorkflowErrors
{
    // Use "WorkflowErrorCode" key to avoid collision with the "ErrorCode" key already
    // set by NotFoundError / ValidationError / ForbiddenError base constructors.
    // HTTP status mapping in ApiControllerBase uses the error type (is NotFoundError etc.),
    // not the metadata key, so this is safe.

    public static IError DefinitionNotFound(Guid id) =>
        new NotFoundError($"WorkflowDefinition '{id}' not found.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.DefinitionNotFound);

    public static IError InstanceNotFound(Guid id) =>
        new NotFoundError($"WorkflowInstance '{id}' not found.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.InstanceNotFound);

    public static IError InvalidTransition(string actionName, Guid fromStateId) =>
        new ValidationError(
            $"Action '{actionName}' is not a valid transition from state '{fromStateId}'.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.InvalidTransition);

    public static IError TransitionForbidden(string actionName, string requiredRole) =>
        new ForbiddenError(
            $"Action '{actionName}' requires role '{requiredRole}'.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.TransitionForbidden);

    public static IError NoInitialState() =>
        new ValidationError("WorkflowDefinition must have exactly one initial state.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.NoInitialState);

    public static IError DuplicateStateName(string name) =>
        new ValidationError($"State name '{name}' already exists in this definition.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.DuplicateStateName);

    public static IError DuplicateTransition(string actionName, Guid fromStateId) =>
        new ValidationError(
            $"Transition '{actionName}' from state '{fromStateId}' already exists.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.DuplicateTransition);

    public static IError DefinitionHasInstances(Guid definitionId) =>
        new ConflictError(
            $"Cannot modify definition '{definitionId}' — it has active running instances.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.DefinitionHasInstances);

    public static IError StateNotInDefinition(Guid stateId, Guid definitionId) =>
        new ValidationError(
            $"State '{stateId}' does not belong to definition '{definitionId}'.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.StateNotInDefinition);

    public static IError DefinitionNotLatest(Guid id) =>
        new ValidationError($"WorkflowDefinition '{id}' is not the latest version. Clone from latest.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.DefinitionNotLatest);

    public static IError ConditionNotMet(IEnumerable<string> failedConditions) =>
        new ValidationError(
            $"Transition conditions not met: {string.Join("; ", failedConditions)}")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.ConditionNotMet);

    // Raised when EF concurrency token on WorkflowInstance.CurrentStateId detects a simultaneous
    // transition race — distinct from InvalidTransition (app-layer sequential-race guard).
    public static IError StateConflict(Guid instanceId) =>
        new ConflictError(
            $"WorkflowInstance '{instanceId}' was modified by a concurrent transition.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.StateConflict);

    // Slice 7: Raised at the outer SaveChangesAsync boundary (IWorkflowInstanceRepository.SaveChangesAsync)
    // where no instance PublicId is in scope. Same error code as StateConflict — both map to GOV_WFL_013
    // (HTTP 409). Distinct factory avoids the Guid param required by StateConflict(Guid).
    public static IError StateConflictAtBoundary() =>
        new ConflictError("Concurrent state modification detected.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.StateConflict);

    // Raised at the SaveChangesAsync boundary when an INSERT collides with the unique active-entity
    // index (SqlException 2601/2627) — a second active instance for the same (EntityType, EntityId,
    // TenantId) while DossierId IS NULL. Defense-in-depth on the non-BusinessProcess create-race;
    // BusinessProcess runs carry a per-run DossierId so they never hit this. Maps to HTTP 409.
    public static IError InstanceAlreadyActive() =>
        new ConflictError("An active workflow instance already exists for this entity.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.InstanceAlreadyActive);

    // Raised when Model B handler resolver returns empty pool for a non-final target state.
    // Indicates workflow misconfiguration — no assignment rules cover the outgoing transitions.
    // Phase 5b: dual-metadata fix (4-B-A INVARIANT) — ValidationError ctor pre-sets ErrorCode to
    // GOV_SYS_002; overwrite via Metadata indexer (WithMetadata throws on duplicate key) so the
    // response body surfaces GOV_WFL_014 not the generic GOV_SYS_002.
    public static IError NoHandlerResolved(long stateId)
    {
        var error = new ValidationError(
            $"No eligible handler resolved for state '{stateId}' — workflow misconfigured.");
        error.Metadata["ErrorCode"] = WorkflowErrorCodes.NoHandlerResolved;
        error.WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.NoHandlerResolved);
        return error;
    }

    // Raised when parallel branch fan-out staging fails. Whole transition rolled back (Slice 6 atomic-tx).
    // Replaces the pre-Slice-6 swallow-catch. Maps to HTTP 422.
    // Uses PublicId (Guid) consistent with all other external-facing error factories in this class.
    public static IError BranchFanOutFailed(Guid instanceId) =>
        new ValidationError(
            $"Parallel branch fan-out failed for instance '{instanceId}' — transition rolled back.")
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.BranchFanOutFailed);

    // P4 Slice 4-B: raised when Claim fails — either sequential (domain guard) or concurrent
    // (RowVersion mismatch at SaveChanges). Both paths return ConflictError → HTTP 409.
    // Domain guard: task.Claim() throws InvalidOperationException("Task is already claimed.").
    // Concurrent race: DbUpdateConcurrencyException caught at SaveChanges boundary.
    // Dual metadata: "ErrorCode" (surfaces in ApiResponse.Fail → response body "code" field)
    // and "WorkflowErrorCode" (cross-module provenance — consistent with other WorkflowErrors factories).
    public static IError AlreadyClaimed(long taskId) =>
        new ConflictError($"WorkflowTask '{taskId}' is already claimed by another user.")
            .WithMetadata("ErrorCode", WorkflowErrorCodes.AlreadyClaimed)
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.AlreadyClaimed);

    // P4 Slice 4-C M1: raised when a Claim/Unclaim attempt targets a terminal-state task
    // (Done or Cancelled — WorkflowTaskStatus enum values). Discriminated from AlreadyClaimed
    // (claimed-but-active) via re-query in UpdateWithClaimGuardAsync after DbUpdateConcurrencyException.
    // status param surfaces the actual enum name for caller context; maps to HTTP 409.
    // Dual metadata — 4-B-A INVARIANT.
    public static IError TaskFinalState(long taskId, string status) =>
        new ConflictError($"WorkflowTask '{taskId}' cannot be modified — it is in terminal state '{status}'.")
            .WithMetadata("ErrorCode", WorkflowErrorCodes.TaskFinalState)
            .WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.TaskFinalState);

    // P4 Slice 4-C: raised when GetInboxQuery requests a resolver mode not yet implemented
    // (Role/Group/OrgUnit deferred per RT2-15). Maps to HTTP 422.
    // mode param surfaced in message so callers can identify the unimplemented variant.
    // 4-B-A INVARIANT: both "ErrorCode" and "WorkflowErrorCode" must be set to GOV_WFL_018.
    // ValidationError ctor pre-sets "ErrorCode" to GOV_SYS_002 — must overwrite via Metadata[key]
    // (dict indexer replaces; WithMetadata would throw on duplicate key).
    public static IError NotImplementedMode(string mode)
    {
        var error = new ValidationError(
            $"Inbox resolver mode '{mode}' is not yet implemented. Supported: ByPermission.");
        // Overwrite the GOV_SYS_002 set by ValidationError ctor with the specific WFL code.
        error.Metadata["ErrorCode"] = WorkflowErrorCodes.NotImplementedMode;
        error.WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.NotImplementedMode);
        return error;
    }

    // Raised when design-time graph validation fails at a publish surface
    // (Save R1, ToggleActive R1-R4, BP Activate R1-R4). Maps to HTTP 422.
    // 4-B-A INVARIANT: ValidationError ctor pre-sets "ErrorCode"=GOV_SYS_002 — overwrite with GOV_WFL_019.
    public static IError GraphValidationFailed(IReadOnlyList<Validation.GraphRuleViolation> violations)
    {
        var error = new ValidationError(
            $"Workflow graph validation failed: {violations.Count} violation(s). See metadata.violations for per-rule details.");
        // Overwrite GOV_SYS_002 set by ValidationError ctor.
        error.Metadata["ErrorCode"] = WorkflowErrorCodes.GraphValidationFailed;
        error.WithMetadata("WorkflowErrorCode", WorkflowErrorCodes.GraphValidationFailed);
        // GraphErrors: INTERNAL raw violations list — for logs/debug only; never surfaced in HTTP response.
        error.WithMetadata("GraphErrors", violations);
        // PublicMetadata: the sole key that ApiResponse.Fail() extracts into ApiError.Metadata on the wire.
        // IReadOnlyDictionary<string, object?> (nullable value) required — invariant TValue, no covariance.
        error.WithMetadata("PublicMetadata", (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["violations"] = violations.Select(v => new
            {
                v.Rule,
                v.StateName,
                v.TransitionActionName,
                v.Detail,
                v.Metadata,   // violation-level metadata (e.g. UnknownGroupCodes from ByGroup Q2 check)
            }).ToList(),
        });
        return error;
    }
}
