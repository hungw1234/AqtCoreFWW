namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Vector database abstraction — swap Qdrant for any vector store without touching modules.
/// Implementations: QdrantVectorStore (default), InMemoryVectorStore (testing).
/// </summary>
public interface IVectorStore
{
    Task EnsureCollectionAsync(string collection, int vectorSize, CancellationToken ct = default);

    Task UpsertAsync(
        string collection,
        string id,
        float[] vector,
        Dictionary<string, object> payload,
        CancellationToken ct = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collection,
        float[] queryVector,
        int topK = 5,
        float scoreThreshold = 0.7f,
        CancellationToken ct = default);

    Task DeleteAsync(string collection, string id, CancellationToken ct = default);
}

public sealed record VectorSearchResult(
    string Id,
    float Score,
    string Text,
    Dictionary<string, string> Payload);
