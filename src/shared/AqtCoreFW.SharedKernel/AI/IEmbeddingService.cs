using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Text embedding generation — returns float[] vector.
/// Reference impl: OllamaEmbeddingService (nomic-embed-text, 768 dims).
/// CachedEmbeddingService decorator wraps this for 7-day embedding cache.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Dimensionality of vectors produced by this service.</summary>
    int VectorDimension { get; }

    Task<Result<float[]>> EmbedAsync(string text, CancellationToken ct = default);
}
