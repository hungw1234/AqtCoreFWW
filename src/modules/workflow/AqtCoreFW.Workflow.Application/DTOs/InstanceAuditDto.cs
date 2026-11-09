namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>Output format for workflow instance audit exports.</summary>
public enum AuditExportFormat { Json = 0, Csv = 1, Pdf = 2 }

/// <summary>
/// Full audit snapshot for a workflow instance: header + transitions + task assignments.
/// Serialized by ExportInstanceAuditQueryHandler per chosen format (JSON/CSV/PDF).
/// </summary>
public sealed record InstanceAuditDto(
    Guid InstanceId,
    Guid TenantId,
    string DefinitionName,
    string DefinitionKey,
    string EntityType,
    Guid EntityId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string CurrentStateName,
    DateTimeOffset? DueAt,
    IReadOnlyList<AuditTransitionDto> Transitions,
    IReadOnlyList<AuditTaskAssignmentDto> TaskAssignments);

/// <summary>Single transition entry with state names resolved (not just IDs).</summary>
public sealed record AuditTransitionDto(
    string FromStateName,
    string ToStateName,
    string ActionName,
    Guid TransitionedBy,
    string? Comment,
    DateTimeOffset TransitionedAt,
    int AttachmentCount);

/// <summary>Single task assignment audit event (Initial/Reassign/Delegate/Escalation).</summary>
public sealed record AuditTaskAssignmentDto(
    string TaskTitle,
    Guid AssignedToId,
    Guid AssignedById,
    string Type,
    DateTimeOffset AssignedAt,
    string? Reason);

/// <summary>
/// Serialized audit export ready for HTTP FileContentResult.
/// Caller chooses Format via query parameter.
/// </summary>
public sealed record AuditExportResult(
    AuditExportFormat Format,
    string FileName,
    string ContentType,
    byte[] Data);
