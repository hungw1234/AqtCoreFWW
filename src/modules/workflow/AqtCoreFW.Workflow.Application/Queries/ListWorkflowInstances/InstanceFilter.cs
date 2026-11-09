namespace AqtCoreFW.Workflow.Application.Queries.ListWorkflowInstances;

/// <summary>
/// Shared filter criteria for both list and summary queries.
/// All fields are optional — null means "no filter on that column".
/// </summary>
/// <summary>
/// Phase-4b B4c: StateId changed Guid?→long? (T1 WorkflowState.Id bigint PK — no Guid AK).
/// </summary>
public sealed record InstanceFilter(
    string? EntityType = null,
    long? StateId = null,
    InstanceStatus? Status = null);

/// <summary>Active = not completed; Completed = has CompletedAt.</summary>
public enum InstanceStatus
{
    Active = 1,
    Completed = 2
}
