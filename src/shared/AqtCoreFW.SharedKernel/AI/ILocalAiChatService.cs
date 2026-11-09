using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Local LLM chat — Ollama only.
/// MUST be used for Classification >= Confidential (data sovereignty enforcement).
/// AiRoutingService returns GOV_AI_001 if this resolves to a stub and classification >= Confidential.
/// </summary>
public interface ILocalAiChatService
{
    Task<Result<string>> ChatAsync(string prompt, AiContext ctx, CancellationToken ct = default);
}
