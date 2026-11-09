using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Chunks, embeds, and upserts document text into Qdrant for RAG retrieval.
/// Chunking: paragraph-based (\n\n split), max 1200 chars, min 50 chars filter.
/// </summary>
public interface IDocumentIngestionService
{
    Task<Result> IngestAsync(Guid fileId, string text, Guid tenantId, CancellationToken ct = default);
}
