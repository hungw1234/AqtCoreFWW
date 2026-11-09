using AqtCoreFW.SharedKernel.AI;
using FluentResults;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Stub for ICloudAiChatService — sovereign compliance default.
/// Always returns GOV_AI_CLOUD_DISABLED.
/// Replace with AzureOpenAiChatService / GeminiChatService when approved for non-sensitive data.
/// Teams register their cloud impl BEFORE AddAiInfrastructure() to override.
/// </summary>
public sealed class CloudAiChatServiceStub : ICloudAiChatService
{
    public Task<Result<string>> ChatAsync(string prompt, AiContext ctx, CancellationToken ct = default)
        => Task.FromResult(Result.Fail<string>(
            "GOV_AI_CLOUD_DISABLED: Cloud AI disabled for sovereign compliance. " +
            "Register a concrete ICloudAiChatService implementation to enable."));
}
