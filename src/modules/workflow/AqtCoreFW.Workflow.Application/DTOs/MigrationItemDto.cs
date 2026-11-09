using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read projection of WorkflowVersionMigrationItem (per-instance migration result).
/// class for Dapper compatibility.
/// Phase-4b B4c: T3 entity — Id is Guid sourced from MessageId (public AK).
///   InstanceId: long (FK to T2 WorkflowInstance.Id — internal).
///   OldStateId/NewStateId: long (FK to T1 WorkflowState.Id — internal).
/// </summary>
public sealed class MigrationItemDto
{
    public Guid Id { get; set; }          // MessageId — T3 public AK
    public long InstanceId { get; set; }
    public long OldStateId { get; set; }
    public long NewStateId { get; set; }
    public MigrationItemStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
