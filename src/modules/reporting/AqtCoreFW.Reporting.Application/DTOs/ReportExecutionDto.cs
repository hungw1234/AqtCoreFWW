using AqtCoreFW.Reporting.Domain.Enums;

namespace AqtCoreFW.Reporting.Application.DTOs;

public record ReportExecutionDto(
    Guid Id,
    Guid ReportDefinitionId,
    string Parameters,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    ExecutionStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    bool HasResult,
    string? ErrorMessage);
