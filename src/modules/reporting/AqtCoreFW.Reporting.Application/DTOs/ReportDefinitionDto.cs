using AqtCoreFW.Reporting.Domain.Enums;

namespace AqtCoreFW.Reporting.Application.DTOs;

public record ReportDefinitionDto(
    Guid Id,
    string Name,
    string NameVi,
    string Description,
    string ParametersSchema,
    OutputFormat OutputFormat,
    bool IsActive,
    string? TenantId,
    DateTimeOffset CreatedAt);
