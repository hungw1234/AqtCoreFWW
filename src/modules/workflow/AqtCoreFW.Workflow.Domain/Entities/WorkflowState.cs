using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// A single state within a WorkflowDefinition.
/// Machine name (Name) is used in code; DisplayName* for UI rendering.
/// Color is a hex string (e.g. "#FF9900") for UI badge coloring.
/// SlaHours: optional per-state SLA; breach triggers auto-transition if AutoTransitionOnTimeoutId is set.
/// AutoTransitionOnTimeoutId: optional transition executed automatically on SLA breach.
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   DefinitionId: long (FK to T1/T2 WorkflowDefinition.Id).
///   AutoTransitionOnTimeoutId: long? (FK to T1 WorkflowTransition.Id).
/// </summary>
public sealed class WorkflowState : Entity
{
    public long DefinitionId { get; private set; }
    public string Name { get; private set; } = string.Empty;          // e.g. "UnderReview"
    public string DisplayNameVi { get; private set; } = string.Empty;
    public string DisplayNameEn { get; private set; } = string.Empty;
    public bool IsInitial { get; private set; }
    public bool IsFinal { get; private set; }
    public string Color { get; private set; } = "#808080";            // hex, default grey
    public int SortOrder { get; private set; }
    public int? SlaHours { get; private set; }                        // null = no per-state SLA
    public long? AutoTransitionOnTimeoutId { get; private set; }      // null = no auto-transition

    // Visual designer canvas coordinates (FE persists on drag-end). Null = let FE auto-layout.
    public double? PositionX { get; private set; }
    public double? PositionY { get; private set; }

    private WorkflowState() { } // EF Core

    public static WorkflowState Create(
        long definitionId,
        string name,
        string displayNameVi,
        string displayNameEn,
        bool isInitial,
        bool isFinal,
        string color,
        int sortOrder,
        int? slaHours = null,
        long? autoTransitionOnTimeoutId = null,
        double? positionX = null,
        double? positionY = null)
    {
        return new WorkflowState
        {
            DefinitionId = definitionId,
            Name = name,
            DisplayNameVi = displayNameVi,
            DisplayNameEn = displayNameEn,
            IsInitial = isInitial,
            IsFinal = isFinal,
            Color = color,
            SortOrder = sortOrder,
            SlaHours = slaHours,
            AutoTransitionOnTimeoutId = autoTransitionOnTimeoutId,
            PositionX = positionX,
            PositionY = positionY
        };
    }

    /// <summary>Update mutable properties of an existing state.</summary>
    public void Update(
        string name, string displayNameVi, string displayNameEn,
        bool isInitial, bool isFinal, string color, int sortOrder,
        int? slaHours = null, long? autoTransitionOnTimeoutId = null,
        double? positionX = null, double? positionY = null)
    {
        Name = name;
        DisplayNameVi = displayNameVi;
        DisplayNameEn = displayNameEn;
        IsInitial = isInitial;
        IsFinal = isFinal;
        Color = color;
        SortOrder = sortOrder;
        SlaHours = slaHours;
        AutoTransitionOnTimeoutId = autoTransitionOnTimeoutId;
        PositionX = positionX;
        PositionY = positionY;
    }
}
