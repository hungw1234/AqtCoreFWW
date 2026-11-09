namespace AqtCoreFW.Ai.Application.DTOs;

/// <summary>Read-side projection of AiPromptTemplate for API responses. Id = PublicId Guid AK (T2 entity).</summary>
public sealed record AiPromptTemplateDto(
    Guid Id,
    string Name,
    string Category,
    string SystemPrompt,
    string UserPromptTemplate,
    int Version,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
