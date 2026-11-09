using AqtCoreFW.SharedKernel.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AqtCoreFW.Ai.Infrastructure.VectorStore;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Ollama SSE streaming chat — implements ILocalAiStreamingChatService.
/// Serialized through OllamaInferenceQueueService (one LLM call at a time on CPU).
/// Calls Ollama /api/chat with stream:true, yields StreamingChatToken per chunk.
/// HttpClient timeout: 120s (CPU inference is slow).
/// </summary>
public sealed class OllamaStreamingChatService(
    OllamaInferenceQueueService inferenceQueue,
    IOptions<OllamaOptions> options,
    ILogger<OllamaStreamingChatService> logger)
    : ILocalAiStreamingChatService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async IAsyncEnumerable<StreamingChatToken> StreamAsync(
        AiContext ctx,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Channel used to bridge the queued work to async enumerable
        var channel = System.Threading.Channels.Channel.CreateUnbounded<StreamingChatToken>();

        // Enqueue the streaming work — runs serialized through the inference queue
        _ = inferenceQueue.EnqueueAsync(async innerCt =>
        {
            try
            {
                await StreamFromOllamaAsync(userMessage, ctx, channel.Writer, innerCt);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
            return (object?)null;
        }, ct).ContinueWith(t =>
        {
            if (t.IsFaulted)
                channel.Writer.TryComplete(t.Exception);
            else if (t.IsCanceled)
                channel.Writer.TryComplete(new OperationCanceledException());
        }, TaskContinuationOptions.ExecuteSynchronously);

        await foreach (var token in channel.Reader.ReadAllAsync(ct))
            yield return token;
    }

    private async Task StreamFromOllamaAsync(
        string userMessage,
        AiContext ctx,
        System.Threading.Channels.ChannelWriter<StreamingChatToken> writer,
        CancellationToken ct)
    {
        var endpoint = options.Value.Endpoint.TrimEnd('/');
        var model = options.Value.ChatModel;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        var body = new
        {
            model,
            messages = new[] { new { role = "user", content = userMessage } },
            stream = true,
            keep_alive = "5m"
        };

        logger.LogDebug(
            "OllamaStreamingChat: tenant={TenantId} classification={Classification}",
            ctx.TenantId, ctx.Classification);

        using var response = await http.PostAsJsonAsync($"{endpoint}/api/chat", body, JsonOpts, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break; // end of stream
            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaStreamChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaStreamChunk>(line, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }

            if (chunk is null)
                continue;

            var token = chunk.Message?.Content ?? string.Empty;
            var isDone = chunk.Done;

            await writer.WriteAsync(
                new StreamingChatToken(token, isDone, isDone ? chunk.DoneReason : null), ct);

            if (isDone)
                break;
        }
    }

    // Ollama /api/chat stream response DTOs
    private sealed record OllamaStreamChunk(
        [property: JsonPropertyName("message")] OllamaStreamMessage? Message,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("done_reason")] string? DoneReason);

    private sealed record OllamaStreamMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
