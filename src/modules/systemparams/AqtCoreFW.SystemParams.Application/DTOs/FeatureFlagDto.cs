namespace AqtCoreFW.SystemParams.Application.DTOs;

/// <summary>DTO for feature flag (SystemParameter with key prefix "feature:").</summary>
public sealed record FeatureFlagDto(
    string Name,
    string Key,
    bool IsEnabled,
    string? Description,
    string? TenantId);
