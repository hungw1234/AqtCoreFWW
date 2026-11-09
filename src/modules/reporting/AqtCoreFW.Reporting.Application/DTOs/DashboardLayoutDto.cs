namespace AqtCoreFW.Reporting.Application.DTOs;

/// <summary>T1 entity — Id is long internal PK (no PublicId). OwnerId/TenantId are Guid FK exceptions.</summary>
public record DashboardLayoutDto(
    long Id,
    string Name,
    Guid OwnerId,
    bool IsDefault,
    string WidgetPositions,
    Guid TenantId,
    DateTimeOffset CreatedAt);
