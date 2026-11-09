using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Defines an allowed transition between two states in a WorkflowDefinition.
/// ConditionsJson: optional declarative guard (key-value only, no dynamic eval).
/// IsRecall: if true, only the original instance creator may execute this transition.
/// Handler authorization is determined via WorkflowTransitionAssignment (assignment-based).
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   DefinitionId: long, FromStateId: long, ToStateId: long (FKs to T1 entities).
/// </summary>
public sealed class WorkflowTransition : Entity
{
    public long DefinitionId { get; private set; }
    public long FromStateId { get; private set; }
    public long ToStateId { get; private set; }
    public string ActionName { get; private set; } = string.Empty;    // e.g. "Approve"
    public string ActionLabelVi { get; private set; } = string.Empty;
    public string ActionLabelEn { get; private set; } = string.Empty;
    public string? ConditionsJson { get; private set; }               // declarative guard only
    public int SortOrder { get; private set; }
    public bool IsRecall { get; private set; }                        // only creator can execute

    // Visual designer handle anchors (FE persists on draw / reconnect). Null = ReactFlow auto-picks default.
    // Format: "t-in"|"t-out"|"r-in"|"r-out"|"b-in"|"b-out"|"l-in"|"l-out" (max 8 chars).
    public string? SourceHandle { get; private set; }
    public string? TargetHandle { get; private set; }

    private WorkflowTransition() { } // EF Core

    public static WorkflowTransition Create(
        long definitionId,
        long fromStateId,
        long toStateId,
        string actionName,
        string actionLabelVi,
        string actionLabelEn,
        string? conditionsJson,
        int sortOrder,
        bool isRecall = false,
        string? sourceHandle = null,
        string? targetHandle = null)
    {
        // Defense-in-depth (BUG2-CLONE 2026-05-18): a transition must reference persisted states.
        // A 0/negative FK means the caller built the transition before its states got their
        // SQL Server IDENTITY ids — the exact silent-corruption class behind WorkflowDefinitions
        // Id=13. Fail fast here so any future regression surfaces as an error, not bad data.
        if (fromStateId <= 0)
            throw new ArgumentException("FromStateId must be a persisted positive id.", nameof(fromStateId));
        if (toStateId <= 0)
            throw new ArgumentException("ToStateId must be a persisted positive id.", nameof(toStateId));

        return new WorkflowTransition
        {
            DefinitionId = definitionId,
            FromStateId = fromStateId,
            ToStateId = toStateId,
            ActionName = actionName,
            ActionLabelVi = actionLabelVi,
            ActionLabelEn = actionLabelEn,
            ConditionsJson = conditionsJson,
            SortOrder = sortOrder,
            IsRecall = isRecall,
            SourceHandle = sourceHandle,
            TargetHandle = targetHandle
        };
    }

    /// <summary>Update mutable properties of an existing transition.</summary>
    public void Update(
        long fromStateId, long toStateId,
        string actionName, string actionLabelVi, string actionLabelEn,
        string? conditionsJson, int sortOrder,
        bool isRecall = false,
        string? sourceHandle = null, string? targetHandle = null)
    {
        FromStateId = fromStateId;
        ToStateId = toStateId;
        ActionName = actionName;
        ActionLabelVi = actionLabelVi;
        ActionLabelEn = actionLabelEn;
        ConditionsJson = conditionsJson;
        SortOrder = sortOrder;
        IsRecall = isRecall;
        SourceHandle = sourceHandle;
        TargetHandle = targetHandle;
    }
}
