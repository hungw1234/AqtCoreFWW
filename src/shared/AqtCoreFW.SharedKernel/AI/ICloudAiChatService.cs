using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Cloud LLM chat (Azure OpenAI, Gemini, etc.) — Public/Internal classification only.
/// AiRoutingService NEVER routes Classification >= Confidential here.
/// Default: CloudAiChatServiceStub returns GOV_AI_CLOUD_DISABLED.
/// </summary>
public interface ICloudAiChatService
{
    Task<Result<string>> ChatAsync(string prompt, AiContext ctx, CancellationToken ct = default);
}
