using AqtCoreFW.SharedKernel.AI;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Profile-aware AI router — extends data sovereignty routing with model profile selection.
/// Routing rules:
///   Classification >= Confidential → local (Ollama) only, GOV_AI_001 if not configured.
///   Classification &lt; Confidential → preferred provider from DB profiles → default profile → local fallback.
/// All routing decisions logged for GOV audit trail.
/// </summary>
public sealed class AiModelRouter(
    ILocalAiChatService localChat,
    ICloudAiChatService cloudChat,
    IDataSovereigntyChecker sovereigntyChecker,
    IAiModelProfileProvider profileProvider,
    ILogger<AiModelRouter> logger)
    : IAiRoutingService
{
    public async Task<Result<string>> RouteAsync(
        string prompt,
        ClassificationLevel classification,
        AiContext ctx,
        CancellationToken ct = default)
    {
        var requiresLocal = sovereigntyChecker.RequiresLocalProcessing(classification);

        if (requiresLocal)
            return await RouteToLocal(prompt, ctx, classification, ct);

        return await RouteToPreferredOrFallback(prompt, ctx, ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Result<string>> RouteToLocal(
        string prompt,
        AiContext ctx,
        ClassificationLevel classification,
        CancellationToken ct)
    {
        logger.LogInformation(
            "AiModelRouter: routing to Local(Ollama) tenant={TenantId} classification={Classification}",
            ctx.TenantId, classification);

        var result = await localChat.ChatAsync(prompt, ctx, ct);

        // GOV_AI_001: sovereign data must not fall back to stub
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

    private async Task<Result<string>> RouteToPreferredOrFallback(
        string prompt,
        AiContext ctx,
        CancellationToken ct)
    {
        // Try to select best profile from DB
        var profiles = await profileProvider.GetActiveProfilesAsync(ct);
        var preferred = profiles.FirstOrDefault(p => p.Provider != "Ollama")
                        ?? profiles.FirstOrDefault();

        var destination = preferred?.Provider ?? "Cloud";
        logger.LogInformation(
            "AiModelRouter: routing to {Destination} tenant={TenantId} model={ModelId}",
            destination, ctx.TenantId, preferred?.ModelId ?? "none");

        // Route to cloud (Azure OpenAI / OpenAI profiles)
        var cloudResult = await cloudChat.ChatAsync(prompt, ctx, ct);
        if (cloudResult.IsSuccess)
            return cloudResult;

        // Fallback to local
        logger.LogWarning(
            "AiModelRouter: cloud failed, falling back to local. tenant={TenantId} reason={Reason}",
            ctx.TenantId, cloudResult.Errors.FirstOrDefault()?.Message);

        return await localChat.ChatAsync(prompt, ctx, ct);
    }

    private static bool IsStubResponse(string response) =>
        response.Contains("AI not configured", StringComparison.OrdinalIgnoreCase) ||
        response.Contains("GOV_AI_NOT_IMPL", StringComparison.OrdinalIgnoreCase);
}
