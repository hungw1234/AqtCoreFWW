namespace AqtCoreFW.ModuleName.Application.DTOs;

public sealed record ModuleNameDto(
    long Id,
    Guid TenantId,
    string Title,
    string? Description,
    DateTimeOffset CreatedAt);
