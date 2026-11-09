namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>Result of a dry-run ValidateMigrationMapping command.</summary>
public sealed class MigrationValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<UnmappedStateDto> UnmappedStates { get; set; } = [];
}

/// <summary>
/// A state in source definition that has active instances but is not in the mapping.
/// Phase-4b B4c: T1 WorkflowState — StateId is long (bigint identity PK).
/// </summary>
public sealed class UnmappedStateDto
{
    // arch:accept-long T1 WorkflowState: aggregate-internal id, migration mapping consumers treat as opaque
    public long StateId { get; set; }
    public string StateName { get; set; } = string.Empty;
    public int ActiveInstanceCount { get; set; }
}
