using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection of WorkflowVersionMigration.
/// class (not record) for Dapper compatibility per feedback-dapper-no-positional-records.
/// Phase-4b B4c: T1 entity — Id, SourceDefinitionId, TargetDefinitionId are long.
/// </summary>
public sealed class MigrationDto
{
    public long Id { get; set; }
    public long SourceDefinitionId { get; set; }
    public long TargetDefinitionId { get; set; }
    public MigrationStatus Status { get; set; }
    public string MappingHash { get; set; } = string.Empty;
    public int TotalInstances { get; set; }
    public int MigratedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? HangfireJobId { get; set; }

    /// <summary>Populated by query handler via Hangfire IMonitoringApi (read-side only).</summary>
    public string? HangfireJobState { get; set; }
}
