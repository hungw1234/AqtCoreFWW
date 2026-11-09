namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection for a single workflow definition version entry.
/// Used by GetVersionHistory query output. Dapper-hydrated — must be a class.
/// Phase-4b B4c: T2 entity — Id is Guid sourced from PublicId (Dapper projection maps PublicId → Id).
/// </summary>
public sealed class VersionHistoryDto
{
    public Guid Id { get; set; }          // Sourced from WorkflowDefinition.PublicId
    public string DefinitionKey { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsLatest { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int InstanceCount { get; set; }
}
