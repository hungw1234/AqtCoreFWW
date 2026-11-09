using AqtCoreFW.SharedKernel.Domain;
using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Data sovereignty router — dispatches prompts to local or cloud AI based on classification.
/// Classification >= Confidential → ILocalAiChatService (Ollama).
/// Classification &lt; Confidential → ICloudAiChatService (if available) else ILocalAiChatService.
/// Returns GOV_AI_001 if local service is stub and classification >= Confidential.
/// Every routing decision is logged to AiDecisionAudit (Session 16).
/// </summary>
public interface IAiRoutingService
{
    Task<Result<string>> RouteAsync(
        string prompt,
        ClassificationLevel classification,
        AiContext ctx,
        CancellationToken ct = default);
}
