using Azure.AI.OpenAI;
using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Azure OpenAI implementation of ICloudAiChatService.
/// Public/Internal classification only — AiRoutingService never routes Confidential+ here.
/// Gracefully returns error if AI:AzureOpenAI config section is missing.
/// </summary>
public sealed class AzureOpenAiChatService : ICloudAiChatService
{
    private readonly AzureOpenAIClient? _client;
    private readonly string? _deploymentName;
    private readonly ILogger<AzureOpenAiChatService> _logger;

    public AzureOpenAiChatService(
        IConfiguration configuration,
        ILogger<AzureOpenAiChatService> logger)
    {
        _logger = logger;

        var endpoint = configuration["AI:AzureOpenAI:Endpoint"];
        var apiKey = configuration["AI:AzureOpenAI:ApiKey"];
        _deploymentName = configuration["AI:AzureOpenAI:DeploymentName"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "AzureOpenAiChatService: AI:AzureOpenAI:Endpoint or ApiKey not configured. " +
                "Service will return error on every call.");
            return;
        }

        _client = new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
    }

    public async Task<Result<string>> ChatAsync(
        string prompt,
        AiContext ctx,
        CancellationToken ct = default)
    {
        if (_client is null || string.IsNullOrWhiteSpace(_deploymentName))
            return Result.Fail<string>(
                "GOV_AI_AZURE_NOT_CONFIGURED: Azure OpenAI endpoint/key/deployment not set. " +
                "Configure AI:AzureOpenAI:Endpoint, ApiKey, and DeploymentName.");

        try
        {
            var chatClient = _client.GetChatClient(_deploymentName);

            var messages = new List<ChatMessage>
            {
                new UserChatMessage(prompt)
            };

            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: ct);
            var content = response.Value.Content[0].Text;

            _logger.LogInformation(
                "AzureOpenAI chat: tenant={TenantId} correlation={CorrelationId} tokens={Tokens}",
                ctx.TenantId, ctx.CorrelationId,
                response.Value.Usage.TotalTokenCount);

            return Result.Ok(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AzureOpenAI chat failed: tenant={TenantId} correlation={CorrelationId}",
                ctx.TenantId, ctx.CorrelationId);
            return Result.Fail<string>($"GOV_AI_AZURE_ERROR: {ex.Message}");
        }
    }
}
