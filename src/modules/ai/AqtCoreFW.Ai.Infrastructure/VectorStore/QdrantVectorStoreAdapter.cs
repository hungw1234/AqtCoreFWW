using AqtCoreFW.SharedKernel.AI;
// Alias to disambiguate: Infrastructure and SharedKernel both define VectorSearchResult
using InfraSearchResult = AqtCoreFW.Ai.Infrastructure.VectorStore.VectorSearchResult;
using SkVectorSearchResult = AqtCoreFW.SharedKernel.AI.VectorSearchResult;

namespace AqtCoreFW.Ai.Infrastructure.VectorStore;

/// <summary>
/// Adapts the internal IQdrantVectorStore to the public IVectorStore (SharedKernel) contract.
/// Allows modules outside AI to depend on the abstraction without referencing Infrastructure.
/// EnsureCollection and Delete map Result→void by throwing on failure (infrastructure concern).
/// </summary>
public sealed class QdrantVectorStoreAdapter(IQdrantVectorStore inner) : IVectorStore
{
    public async Task EnsureCollectionAsync(
        string collection, int vectorSize, CancellationToken ct = default)
    {
        var result = await inner.EnsureCollectionAsync(collection, vectorSize, ct);
        if (result.IsFailed)
            throw new InvalidOperationException(result.Errors[0].Message);
    }

    public async Task UpsertAsync(
        string collection, string id, float[] vector,
        Dictionary<string, object> payload, CancellationToken ct = default)
    {
        var result = await inner.UpsertAsync(collection, id, vector, payload, ct);
        if (result.IsFailed)
            throw new InvalidOperationException(result.Errors[0].Message);
    }

    public async Task<IReadOnlyList<SkVectorSearchResult>> SearchAsync(
        string collection, float[] queryVector, int topK = 5,
        float scoreThreshold = 0.7f, CancellationToken ct = default)
    {
        // IQdrantVectorStore returns InfraSearchResult; IVectorStore contract returns SkVectorSearchResult
        IReadOnlyList<InfraSearchResult> results =
            await inner.SearchAsync(collection, queryVector, topK, scoreThreshold, ct);
        return results
            .Select(r => new SkVectorSearchResult(r.Id, r.Score, r.Text, r.Payload))
            .ToList();
    }

    public async Task DeleteAsync(
        string collection, string id, CancellationToken ct = default)
    {
        var result = await inner.DeleteAsync(collection, id, ct);
        if (result.IsFailed)
            throw new InvalidOperationException(result.Errors[0].Message);
    }
}
