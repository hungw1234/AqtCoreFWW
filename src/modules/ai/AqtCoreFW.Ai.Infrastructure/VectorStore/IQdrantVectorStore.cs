using FluentResults;

namespace AqtCoreFW.Ai.Infrastructure.VectorStore;

/// <summary>
/// Internal Qdrant abstraction — keeps RagService and DocumentIngestionService
/// decoupled from raw HttpClient details.
/// </summary>
public interface IQdrantVectorStore
{
    Task<Result> EnsureCollectionAsync(string collection, int vectorSize, CancellationToken ct = default);
    Task<Result> UpsertAsync(string collection, string id, float[] vector, Dictionary<string, object> payload, CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string collection, float[] queryVector, int topK, float scoreThreshold, CancellationToken ct = default);
    Task<Result> DeleteAsync(string collection, string id, CancellationToken ct = default);
}

public sealed record VectorSearchResult(string Id, float Score, string Text, Dictionary<string, string> Payload);
