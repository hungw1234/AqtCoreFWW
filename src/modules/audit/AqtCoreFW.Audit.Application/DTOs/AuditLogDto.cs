using AqtCoreFW.Audit.Domain.ValueObjects;

namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>Security incident list item — Dapper requires class with parameterless ctor (positional record fails on enum + DateTime column mapping).</summary>
public sealed class SecurityIncidentDto
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; }
    public string Description { get; set; } = string.Empty;
    public Guid ReportedBy { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? Mitigations { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Login attempt audit row. WARNING: positional record — Dapper binds by parameter position.
/// Adding/reordering a column requires matching the SQL SELECT order. If the next column
/// is an enum or non-DateTimeOffset DateTime, convert to class with parameterless ctor first
/// (see <see cref="SecurityIncidentDto"/> / <see cref="RtbfRequestDto"/> for pattern).
/// One column-add away from the AUDIT-INCIDENT-DTO-DAPPER (P1, 2026-05-08) regression.
/// </summary>
public sealed record LoginAttemptDto(
    Guid Id,
    Guid? UserId,
    string Email,
    string IpAddress,
    bool Success,
    string? FailureReason,
    string? UserAgent,
    DateTimeOffset AttemptedAt);

/// <summary>RTBF request list item — Dapper requires class with parameterless ctor.</summary>
public sealed class RtbfRequestDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DataSubjectId { get; set; }
    public string? DataSubjectEmail { get; set; }
    public string? CitizenNationalId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset DueBy { get; set; }
    public Guid? ProcessedBy { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? RejectionReason { get; set; }
}

/// <summary>
/// Audit log entry list item. WARNING: positional record — Dapper binds by parameter position.
/// Adding/reordering a column requires matching the SQL SELECT order. If the next column
/// is an enum or non-DateTimeOffset DateTime, convert to class with parameterless ctor first
/// (see <see cref="SecurityIncidentDto"/> / <see cref="RtbfRequestDto"/> for pattern).
/// One column-add away from the AUDIT-INCIDENT-DTO-DAPPER (P1, 2026-05-08) regression.
/// </summary>
public sealed record AuditLogDto(
    Guid Id,
    Guid? TenantId,
    Guid? UserId,
    string UserName,
    string Action,
    string ModuleName,
    string ResourceType,
    string? ResourceId,
    string? IpAddress,
    DateTimeOffset OccurredAt,
    string? CorrelationId);

public sealed record AuditStatisticsDto(
    int TodayTotal,
    int WeekTotal,
    int MonthTotal,
    IReadOnlyList<ActionSummary> ByAction,
    IReadOnlyList<ModuleSummary> ByModule);

public sealed record ActionSummary(string Action, int Count);
public sealed record ModuleSummary(string Module, int Count);
