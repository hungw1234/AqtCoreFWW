namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection for a single state mapping entry (old → new state) with resolved names.
/// Used by validation and UI display. Dapper-hydrated — must be a class.
/// Phase-4b B4c: T1 WorkflowState — OldStateId/NewStateId are long (bigint identity PKs).
/// </summary>
public sealed class StateMappingDto
{
    public long OldStateId { get; set; }
    public string OldStateName { get; set; } = string.Empty;
    public long NewStateId { get; set; }
    public string NewStateName { get; set; } = string.Empty;
}
