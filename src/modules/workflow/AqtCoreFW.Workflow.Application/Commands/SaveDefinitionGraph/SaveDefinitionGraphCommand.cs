using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;

/// <summary>
/// Replaces all states and transitions on a definition (used by visual designer + form tabs).
/// Validates graph integrity before saving. Returns updated definition.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record SaveDefinitionGraphCommand(
    Guid DefinitionId,
    Guid TenantId,
    Guid ModifiedBy,
    IReadOnlyList<GraphStateInput> States,
    IReadOnlyList<GraphTransitionInput> Transitions)
    : ICommand<WorkflowDefinitionDto>
{
    /// <summary>
    /// Additive-nullable parallel-branch authoring payload (fork → N children → join), name-keyed.
    /// Old clients omit it → empty default → no branch rows touched (backward-compat, mirrors the
    /// BP form-options additive-nullable precedent). Folded into the same replace-all transaction
    /// as states/transitions; absent branch = delete, present = upsert.
    /// </summary>
    public IReadOnlyList<GraphBranchInput> Branches { get; init; } = [];
}

/// <summary>
/// Wire shape for one parallel branch (one draggable fork gateway). Name-keyed: BranchingStateName /
/// ContinuationStateName / child TargetStateName / resolution ResultingStateName resolve to the
/// freshly-saved state IDs in the repository (replace-all regenerates state IDENTITY each save).
/// Timeout is fork-level (TimeoutMinutes); PositionX/Y persist the single fork diamond's drag spot.
/// </summary>
public sealed record GraphBranchInput(
    string BranchingStateName,
    string ContinuationStateName,
    ParallelBranchType BranchType,
    bool AllowCancelOnAnyReject,
    int TimeoutMinutes,
    IReadOnlyList<GraphBranchChildInput> Children,
    int? RequiredCount = null,
    double? PositionX = null,
    double? PositionY = null);

public sealed record GraphBranchChildInput(
    string TargetStateName,
    string Name,
    string DisplayNameVi,
    int SortOrder,
    IReadOnlyList<GraphBranchResolutionInput> Resolutions,
    string? DepartmentId = null,
    Guid? DefaultAssigneeId = null)
{
    /// <summary>
    /// Optional per-child handler-assignment payload (clauses ByRole/ByPermission/...) — null = no pool
    /// rule (replace-all: DELETE existing on Save). Reuses <see cref="AssignmentInput"/> so the FE branch
    /// editor and the transition editor share one shape. Validated pre-persist by IWorkflowGraphValidator.
    /// </summary>
    public AssignmentInput? Assignment { get; init; }
}

public sealed record GraphBranchResolutionInput(
    BranchResolutionType ResolutionType,
    string? ResultingStateName = null);

public sealed record GraphStateInput(
    string Name,
    string DisplayNameVi,
    string DisplayNameEn,
    bool IsInitial,
    bool IsFinal,
    string Color,
    int SortOrder,
    int? SlaHours = null,
    /// <summary>
    /// ACCEPT-LONG: T1 WorkflowTransition.Id (long bigint PK — no Guid AK).
    /// Opaque round-trip handle; FE consumes via orval <c>number | string</c> union.
    /// </summary>
    long? AutoTransitionOnTimeoutId = null,
    double? PositionX = null,
    double? PositionY = null);

public sealed record GraphTransitionInput(
    string FromStateName,
    string ToStateName,
    string ActionName,
    string ActionLabelVi,
    string ActionLabelEn,
    string? ConditionsJson,
    int SortOrder,
    bool IsRecall = false,
    string? SourceHandle = null,
    string? TargetHandle = null)
{
    /// <summary>
    /// Phase 5c-followup (Option A) per-transition assignment payload — null = no rule (Q2 replace-all: DELETE existing row on Save).
    /// R2/R3/R4 validated pre-persist by IWorkflowGraphValidator.ValidateForSaveAsync (Phase 03).
    /// </summary>
    public AssignmentInput? Assignment { get; init; }
}

/// <summary>
/// Per-transition handler-assignment payload (Option A, plan 260523-1241).
/// MVP modes (FE-exposed): SpecificUser / Initiator / ByPermission.
/// NotSupported (R4 reject at Save): PreviousHandler / ByRole / ByGroup / ByOrgUnit (clause) + Intersect (combine).
/// </summary>
public sealed record AssignmentInput(
    AssignmentCombineMode CombineMode,
    bool AllowAssignorOverride,
    IReadOnlyList<AssignmentClauseInput> Clauses);

/// <summary>
/// One resolution clause within an <see cref="AssignmentInput"/>.
/// RefsJson is null for ref-less modes (Initiator); otherwise a JSON array of ids/codes
/// (R3 canonicalizes ByPermission codes Trim+ToUpperInvariant in validator).
/// </summary>
public sealed record AssignmentClauseInput(
    AssignmentClauseMode Mode,
    string? RefsJson);
