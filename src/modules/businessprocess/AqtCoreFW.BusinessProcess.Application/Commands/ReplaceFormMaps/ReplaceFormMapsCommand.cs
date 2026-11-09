using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.BusinessProcess.Application.Commands.ReplaceFormMaps;

/// <summary>
/// Replace-all command for state→form and transition→form bindings on a BusinessProcessDefinition.
/// Atomically clears existing bindings and writes the full submitted list (D-B-1).
/// displayOrder is derived server-side from array index — client-supplied order in DTO is ignored (D-B-2).
/// IfMatchETag is mandatory for optimistic concurrency (D-B-3); mismatch returns 412.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record ReplaceFormMapsCommand(
    Guid BusinessProcessPublicId,
    Guid TenantId,
    Guid ModifiedBy,
    string? IfMatchETag,
    IReadOnlyList<StateFormBindingDto> StateForms,
    IReadOnlyList<TransitionFormBindingDto> TransitionForms
) : ICommand;

/// <summary>
/// State→form binding submitted by client.
/// WorkflowStateId is the long internal PK (T1 entity, no Guid AK) — validated against the
/// BP's WorkflowDefinition structure to prevent cross-definition pollution.
/// FormTemplatePublicId is Guid (T2 AK) — resolved to bigint via IFormTemplateQueryClient.
/// </summary>
public sealed record StateFormBindingDto(
    long WorkflowStateId,
    Guid FormTemplatePublicId,
    int Version,
    bool IsRequired);

/// <summary>
/// Transition→form binding submitted by client.
/// WorkflowTransitionId is the long internal PK (T1 entity, no Guid AK).
/// FormTemplatePublicId is Guid (T2 AK) — resolved to bigint via IFormTemplateQueryClient.
/// </summary>
public sealed record TransitionFormBindingDto(
    long WorkflowTransitionId,
    Guid FormTemplatePublicId,
    int Version,
    bool IsRequired);
