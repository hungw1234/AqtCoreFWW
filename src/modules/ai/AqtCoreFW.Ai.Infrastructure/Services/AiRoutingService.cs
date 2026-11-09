using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Data sovereignty router — 4-layer GOV AI enforcement (Security S260307).
/// Layer 2: routes Classification >= Confidential to ILocalAiChatService only.
/// Returns GOV_AI_001 if local service is a stub and classification >= Confidential.
/// Logs every routing decision (tenant, classification, destination, correlation).
/// </summary>
public sealed class AiRoutingService(
    ILocalAiChatService localChat,
    ICloudAiChatService cloudChat,
    IDataSovereigntyChecker sovereigntyChecker,
    ILogger<AiRoutingService> logger)
    : IAiRoutingService
{
    public async Task<Result<string>> RouteAsync(
        string prompt,
        ClassificationLevel classification,
        AiContext ctx,
        CancellationToken ct = default)
    {
        var requiresLocal = sovereigntyChecker.RequiresLocalProcessing(classification);
        var destination = requiresLocal ? "Local(Ollama)" : "Cloud";

        logger.LogInformation(
            "AI routing: tenant={TenantId} classification={Classification} destination={Destination} correlation={CorrelationId}",
            ctx.TenantId, classification, destination, ctx.CorrelationId);

        if (requiresLocal)
        {
            var result = await localChat.ChatAsync(prompt, ctx, ct);

            // GOV_AI_001: if local is a stub (returns "AI not configured"), block the request
            if (result.IsSuccess && IsStubResponse(result.Value))
            {
                logger.LogError(
                    "GOV_AI_001 sovereignty violation: tenant={TenantId} classification={Classification} — local AI is stub",
                    ctx.TenantId, classification);
                return Result.Fail(
                    "GOV_AI_001: Data sovereignty violation — classification >= Confidential requires local AI. Configure Ollama.");
            }

            return result;
        }

        // Public/Internal: try cloud first, fallback to local
        var cloudResult = await cloudChat.ChatAsync(prompt, ctx, ct);
        if (cloudResult.IsSuccess)
            return cloudResult;

        logger.LogWarning(
            "Cloud AI unavailable, falling back to local: tenant={TenantId} reason={Reason}",
            ctx.TenantId, cloudResult.Errors.FirstOrDefault()?.Message);

        return await localChat.ChatAsync(prompt, ctx, ct);
    }

    /// <summary>Detects stub responses to enforce GOV_AI_001 for sovereign data.</summary>
    private static bool IsStubResponse(string response) =>
        response.Contains("AI not configured", StringComparison.OrdinalIgnoreCase) ||
        response.Contains("GOV_AI_NOT_IMPL", StringComparison.OrdinalIgnoreCase);
}
