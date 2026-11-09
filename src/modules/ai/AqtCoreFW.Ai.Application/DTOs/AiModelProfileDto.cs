namespace AqtCoreFW.Ai.Application.DTOs;

/// <summary>Read-side projection of AiModelProfile for API responses. Id = T2 PublicId Guid (AK external addressing).</summary>
public sealed record AiModelProfileDto(
    Guid Id,
    string Name,
    string Provider,
    string ModelId,
    string? Endpoint,
    int MaxTokens,
    decimal Temperature,
    decimal CostPerToken,
    bool IsDefault,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
