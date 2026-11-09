namespace AqtCoreFW.AssetManagement.Application.DTOs;

public sealed record AssetManagementDto(
    long Id,
    Guid TenantId,
    string Title,
    string? Description,
    DateTimeOffset CreatedAt);
