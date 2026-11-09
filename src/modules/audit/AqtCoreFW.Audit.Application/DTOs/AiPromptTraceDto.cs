namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>
/// Read model for AiPromptTrace — PromptText intentionally excluded (sensitive data).
/// </summary>
public sealed record AiPromptTraceDto(
    Guid Id,
    Guid SessionId,
    Guid? ModelProfileId,
    string ModelName,
    string PromptHash,
    string? ResponseHash,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    decimal Cost,
    string ClassificationLevel,
    Guid TenantId,
    DateTimeOffset CreatedAt);
