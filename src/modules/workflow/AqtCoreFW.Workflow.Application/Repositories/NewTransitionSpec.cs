using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Transport record passed from SaveDefinitionGraph handler to the repository.
///
/// Why a spec instead of a pre-built <see cref="AqtCoreFW.Workflow.Domain.Entities.WorkflowTransition"/>:
/// transitions reference state primary keys (FromStateId / ToStateId), but state IDs are
/// IDENTITY-assigned by SQL Server during INSERT. The handler does not know these values
/// when it builds the graph — only the state names. The repository performs the save in
/// two phases (states first → IDs assigned → name→Id map → transitions) and resolves the
/// FK using the names supplied here.
///
/// Phase 5c-followup (Option A, plan 260523-1241): <see cref="Assignment"/> carries the
/// optional <see cref="WorkflowTransitionAssignment"/> payload. NEW Phase 3 of the repo's
/// ReplaceGraphAsync persists this AFTER transition IDENTITY is assigned, within the same
/// TransactionScope. Null = Q2 replace-all DELETE.
/// </summary>
public sealed record NewTransitionSpec(
    string FromStateName,
    string ToStateName,
    string ActionName,
    string ActionLabelVi,
    string ActionLabelEn,
    string? ConditionsJson,
    int SortOrder,
    bool IsRecall,
    string? SourceHandle = null,
    string? TargetHandle = null)
{
    public AssignmentSpec? Assignment { get; init; }
}

/// <summary>
/// Repo-transport mirror of <see cref="AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph.AssignmentInput"/>.
/// Validator (Phase 03) MAY rewrite each clause's RefsJson via canonicalization before this
/// spec is passed to the repo — repo persists whatever the spec carries (no further validation).
/// </summary>
public sealed record AssignmentSpec(
    AssignmentCombineMode CombineMode,
    bool AllowAssignorOverride,
    IReadOnlyList<AssignmentClauseSpec> Clauses);

/// <summary>
/// Repo-transport mirror of <see cref="AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph.AssignmentClauseInput"/>.
///
/// MUTABLE on RefsJson: validator R3 canonicalization (Phase 03) rewrites RefsJson in place via
/// <see cref="SetCanonicalRefsJson(string?)"/> so the repo Phase 3 (Phase 04) persists the canonical
/// (Trim + ToUpperInvariant) form. Mirrors the Domain entity pattern at
/// <see cref="AqtCoreFW.Workflow.Domain.Entities.WorkflowTransitionAssignmentClause.SetCanonicalRefsJson(string?)"/>.
///
/// Positional ctor preserved for backwards-compat with Phase 02 contract tests (record→class swap).
/// </summary>
public sealed class AssignmentClauseSpec
{
    public AssignmentClauseMode Mode { get; }
    public string? RefsJson { get; private set; }

    public AssignmentClauseSpec(AssignmentClauseMode mode, string? refsJson)
    {
        Mode = mode;
        RefsJson = string.IsNullOrWhiteSpace(refsJson) ? null : refsJson;
    }

    /// <summary>
    /// Overwrites RefsJson with the canonical (trimmed + upper-cased) form produced by the graph
    /// validator. Called by WorkflowGraphValidator during R3 canonicalization writeback at Save-time.
    /// Uses the same null-guard convention as the ctor: null/whitespace → null.
    /// </summary>
    public void SetCanonicalRefsJson(string? canonicalRefsJson)
    {
        RefsJson = string.IsNullOrWhiteSpace(canonicalRefsJson) ? null : canonicalRefsJson;
    }
}
