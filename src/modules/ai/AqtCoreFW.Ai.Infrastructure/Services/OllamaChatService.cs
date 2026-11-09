using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Local LLM chat via Microsoft.Extensions.AI + Ollama.
/// Model: qwen2.5:7b (better Vietnamese support than llama3).
/// Timeout: 120s (CPU-only inference is 10-50x slower than GPU).
/// Per-request keep_alive=5m (on-demand — conserves RAM on CPU nodes).
/// Routed here by AiRoutingService for Classification >= Confidential.
/// </summary>
public sealed class OllamaChatService(
    IChatClient chatClient,
    ILogger<OllamaChatService> logger)
    : ILocalAiChatService
{
    public async Task<Result<string>> ChatAsync(
        string prompt,
        AiContext ctx,
        CancellationToken ct = default)
    {
        try
        {
            logger.LogDebug(
                "OllamaChatService: tenant={TenantId} classification={Classification} correlation={CorrelationId}",
                ctx.TenantId, ctx.Classification, ctx.CorrelationId);

            var messages = new[] { new ChatMessage(ChatRole.User, prompt) };
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var text = response.Text ?? string.Empty;
            return Result.Ok(text);
        }
        catch (OperationCanceledException)
        {
            return Result.Fail("GOV_AI_002: Ollama request timed out (120s limit for CPU inference).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaChatService failed for tenant={TenantId}", ctx.TenantId);
            return Result.Fail($"GOV_AI_002: Ollama unavailable — {ex.Message}");
        }
    }
}
