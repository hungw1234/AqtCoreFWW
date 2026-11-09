using AqtCoreFW.Reporting.Domain.Enums;

namespace AqtCoreFW.Reporting.Application.DTOs;

/// <summary>T1 entity — Id is long internal PK (no PublicId). TenantId is Guid FK exception.</summary>
public record ReportWidgetDto(
    long Id,
    string Name,
    WidgetType Type,
    string DataQuery,
    string? Config,
    int RefreshSeconds,
    Guid TenantId,
    DateTimeOffset CreatedAt);
