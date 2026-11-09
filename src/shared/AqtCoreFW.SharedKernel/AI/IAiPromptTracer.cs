namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Cross-module contract for tracing AI prompts.
/// M15 Audit implements this to log every AI request for GOV compliance.
/// </summary>
public interface IAiPromptTracer
{
    Task TraceAsync(AiPromptTraceInput input, CancellationToken ct = default);
}

public sealed record AiPromptTraceInput(
    Guid SessionId,
    string ModelName,
    string PromptHash,
    string? PromptText,
    string? ResponseHash,
    int InputTokens,
    int OutputTokens,
    int LatencyMs,
    decimal Cost,
    string ClassificationLevel);
