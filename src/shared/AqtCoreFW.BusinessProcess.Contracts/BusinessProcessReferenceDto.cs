namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Identifies a BusinessProcessDefinition that references a given FormTemplate or WorkflowDefinition.
/// Used by Forms and Workflow modules to show pre-flight blocking dialogs before deactivate/delete.
/// </summary>
/// <param name="BusinessProcessPublicId">Public (Guid) identifier — safe to expose to FE.</param>
/// <param name="Code">Short admin code, e.g. "BP-001".</param>
/// <param name="Name">Display name.</param>
/// <param name="Role">
/// Reference role: "intake" | "state-form" | "transition-form" | "workflow".
/// FE maps to i18n keys businessProcessAdmin.refs.role.*.
/// </param>
/// <param name="WorkflowStateId">Internal state id — only set for role="state-form".</param>
/// <param name="WorkflowTransitionId">Internal transition id — only set for role="transition-form".</param>
/// <param name="IsActive">Whether the BP definition is currently active.</param>
public sealed record BusinessProcessReferenceDto(
    Guid BusinessProcessPublicId,
    string Code,
    string Name,
    string Role,
    long? WorkflowStateId,
    long? WorkflowTransitionId,
    bool IsActive);
