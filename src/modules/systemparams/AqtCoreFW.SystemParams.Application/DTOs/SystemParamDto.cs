namespace AqtCoreFW.SystemParams.Application.DTOs;

/// <summary>DTO for SystemParameter (excludes feature flags).</summary>
public sealed record SystemParamDto(
    string Key,
    string Value,
    string DataType,
    string? Description,
    bool IsEditable,
    string? TenantId);

/// <summary>Result after updating a system parameter.</summary>
public sealed record UpdateSystemParamResult(string Key, string Value);
