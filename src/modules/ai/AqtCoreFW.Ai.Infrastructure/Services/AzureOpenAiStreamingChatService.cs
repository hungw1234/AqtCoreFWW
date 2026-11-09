using Azure.AI.OpenAI;
using AqtCoreFW.SharedKernel.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Runtime.CompilerServices;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Azure OpenAI SSE streaming implementation of ICloudAiStreamingChatService.
/// Public/Internal classification only. Gracefully yields error token if not configured.
/// </summary>
public sealed class AzureOpenAiStreamingChatService : ICloudAiStreamingChatService
{
    private readonly AzureOpenAIClient? _client;
    private readonly string? _deploymentName;
    private readonly ILogger<AzureOpenAiStreamingChatService> _logger;

    public AzureOpenAiStreamingChatService(
        IConfiguration configuration,
        ILogger<AzureOpenAiStreamingChatService> logger)
    {
        _logger = logger;

        var endpoint = configuration["AI:AzureOpenAI:Endpoint"];
        var apiKey = configuration["AI:AzureOpenAI:ApiKey"];
        _deploymentName = configuration["AI:AzureOpenAI:DeploymentName"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "AzureOpenAiStreamingChatService: AI:AzureOpenAI config missing. " +
                "Streaming will yield error token on every call.");
            return;
        }

        _client = new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
    }

    public async IAsyncEnumerable<StreamingChatToken> StreamAsync(
        AiContext ctx,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_client is null || string.IsNullOrWhiteSpace(_deploymentName))
        {
            yield return new StreamingChatToken(
                "GOV_AI_AZURE_NOT_CONFIGURED: Azure OpenAI not configured.",
                IsComplete: true,
                FinishReason: "config_error");
            yield break;
        }

        var chatClient = _client.GetChatClient(_deploymentName);
        var messages = new List<ChatMessage> { new UserChatMessage(userMessage) };

        StreamingChatCompletionUpdate? lastUpdate = null;

        await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, cancellationToken: ct))
        {
            lastUpdate = update;

            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return new StreamingChatToken(part.Text, IsComplete: false);
            }
        }

        var finishReason = lastUpdate?.FinishReason?.ToString() ?? "stop";
        yield return new StreamingChatToken(string.Empty, IsComplete: true, FinishReason: finishReason);
    }
}
