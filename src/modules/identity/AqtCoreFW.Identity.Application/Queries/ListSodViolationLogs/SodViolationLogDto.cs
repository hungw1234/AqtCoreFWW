namespace AqtCoreFW.Identity.Application.Queries.ListSodViolationLogs;

/// <summary>
/// SoD violation log DTO — includes JOINed rule codes + severity.
/// INNER JOIN to SodConflictRules guarantees codes/severity present (V2 soft-delete only).
/// Id = SodViolationLog.MessageId (T3 external-facing AK, Guid v7).
/// RuleId = SodViolationLog.SodRuleId (long? FK to SodConflictRule T1 PK).
/// </summary>
public sealed record SodViolationLogDto(
    Guid Id,
    DateTime DetectedAt,
    Guid? UserId,
    Guid? RoleId,
    long? RuleId,
    string PermissionCodeA,
    string PermissionCodeB,
    string Severity,
    Guid ActorId,
    string? Justification);
