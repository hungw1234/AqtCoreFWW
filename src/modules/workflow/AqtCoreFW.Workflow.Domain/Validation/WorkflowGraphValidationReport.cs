namespace AqtCoreFW.Workflow.Domain.Validation;

/// <summary>
/// Immutable result of a workflow graph validation pass.
/// Lives in Domain so WorkflowErrors factory can reference GraphRuleViolation without
/// a Domain→Application cycle.
/// </summary>
public sealed record WorkflowGraphValidationReport(
    bool IsValid,
    IReadOnlyList<GraphRuleViolation> Violations)
{
    public static WorkflowGraphValidationReport Ok() =>
        new(true, Array.Empty<GraphRuleViolation>());

    public static WorkflowGraphValidationReport Fail(IReadOnlyList<GraphRuleViolation> violations) =>
        new(false, violations);
}

/// <summary>
/// One rule violation in a workflow graph validation pass.
/// </summary>
/// <param name="Rule">Rule code: "R1" | "R2" | "R3" | "R4".</param>
/// <param name="StateName">Affected state name, if applicable.</param>
/// <param name="TransitionActionName">Affected transition action name, if applicable.</param>
/// <param name="Detail">Human-readable description of the violation.</param>
/// <param name="Metadata">
/// Optional structured metadata for test assertion and client error detail
/// (e.g. <c>UnknownGroupCodes</c> for ByGroup R-extended Save-validation — RT2-15 Phase 02).
/// Null when no structured extras needed. All existing callers omit this parameter (additive extension).
/// </param>
public sealed record GraphRuleViolation(
    string Rule,
    string? StateName,
    string? TransitionActionName,
    string Detail,
    IReadOnlyDictionary<string, object>? Metadata = null);
