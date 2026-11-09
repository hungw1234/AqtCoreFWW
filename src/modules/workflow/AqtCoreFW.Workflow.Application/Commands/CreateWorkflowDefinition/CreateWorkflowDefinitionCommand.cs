using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Workflow.Application.DTOs;

namespace AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;

/// <summary>
/// Creates a new WorkflowDefinition with its initial set of states and transitions.
/// Returns the new definition ID on success.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateWorkflowDefinitionCommand(
    string Name,
    string Description,
    Guid TenantId,
    Guid CreatedBy,
    IReadOnlyList<CreateStateRequest> States,
    IReadOnlyList<CreateTransitionRequest> Transitions,
    string EntityType = "Generic")
    : ICommand<WorkflowDefinitionDto>;

public sealed record CreateStateRequest(
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

public sealed record CreateTransitionRequest(
    string FromStateName,
    string ToStateName,
    string ActionName,
    string ActionLabelVi,
    string ActionLabelEn,
    string? ConditionsJson,
    int SortOrder,
    bool IsRecall = false,
    string? SourceHandle = null,
    string? TargetHandle = null);
