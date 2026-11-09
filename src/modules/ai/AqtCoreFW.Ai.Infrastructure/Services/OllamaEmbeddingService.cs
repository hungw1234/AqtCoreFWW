using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Text embedding via Microsoft.Extensions.AI + Ollama nomic-embed-text (768 dims).
/// Timeout: 30s. Single call per text — no batching on CPU.
/// CachedEmbeddingService decorator wraps this for 7-day IMemoryCache.
/// </summary>
public sealed class OllamaEmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<OllamaEmbeddingService> logger)
    : IEmbeddingService
{
    public int VectorDimension => 768;

    public async Task<Result<float[]>> EmbedAsync(string text, CancellationToken ct = default)
    {
        try
        {
            var embeddings = await embeddingGenerator.GenerateAsync([text], cancellationToken: ct);
            var vector = embeddings[0].Vector.ToArray();
            return Result.Ok(vector);
        }
        catch (OperationCanceledException)
        {
            return Result.Fail("GOV_AI_002: Ollama embedding timed out (30s limit).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaEmbeddingService failed for text length={Length}", text.Length);
            return Result.Fail($"GOV_AI_002: Ollama embedding unavailable — {ex.Message}");
        }
    }
}
