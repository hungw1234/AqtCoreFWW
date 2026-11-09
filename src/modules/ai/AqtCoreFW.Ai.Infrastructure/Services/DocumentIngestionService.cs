using AqtCoreFW.Ai.Infrastructure.VectorStore;
using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Chunks document text and upserts embeddings into Qdrant per tenant collection.
/// Chunking: paragraph-based (\n\n split), max 1200 chars, min 50 chars filter (Session 16).
/// CPU: single embed call per chunk (no batching — GPU batch not applicable).
/// Both failure paths are hard (all-or-nothing, never silently degraded): an embedding failure
/// aborts and propagates the underlying error; Qdrant down → GOV_AI_003.
/// </summary>
public sealed class DocumentIngestionService(
    IEmbeddingService embeddingService,
    IQdrantVectorStore vectorStore,
    ILogger<DocumentIngestionService> logger)
    : IDocumentIngestionService
{
    private const int MaxChunkChars = 1200;
    private const int MinChunkChars = 50;

    public async Task<Result> IngestAsync(
        Guid fileId,
        string text,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var collectionName = $"vectors_{tenantId:N}";

        // Ensure collection exists (scalar quantization + tenant_id payload index)
        var ensureResult = await vectorStore.EnsureCollectionAsync(
            collectionName, embeddingService.VectorDimension, ct);
        if (ensureResult.IsFailed)
            return ensureResult;

        var chunks = ChunkText(text);
        logger.LogInformation(
            "Ingesting fileId={FileId} tenantId={TenantId} chunks={ChunkCount}",
            fileId, tenantId, chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var embedResult = await embeddingService.EmbedAsync(chunk, ct);
            if (embedResult.IsFailed)
            {
                // Fail-hard (all-or-nothing) — skipping a failed chunk would silently produce a
                // partially indexed document still reported as success, leaving RAG retrieval with
                // missing content (unacceptable for GOV data). Mirror the Qdrant-upsert path below:
                // propagate the embedding error and abort so the caller retries the whole document.
                logger.LogError(
                    "Embedding failed for chunk {Index} of fileId={FileId} — aborting ingestion (all-or-nothing).",
                    i, fileId);
                return Result.Fail(embedResult.Errors);
            }

            var pointId = $"{fileId:N}_{i}";
            var payload = new Dictionary<string, object>
            {
                ["fileId"] = fileId.ToString(),
                ["chunkIndex"] = i,
                ["tenant_id"] = tenantId.ToString()
            };

            var upsertResult = await vectorStore.UpsertAsync(
                collectionName, pointId, embedResult.Value, payload, ct);

            if (upsertResult.IsFailed)
            {
                logger.LogError(
                    "GOV_AI_003: Qdrant upsert failed for fileId={FileId} chunk={Index}", fileId, i);
                return Result.Fail("GOV_AI_003: Vector store unavailable — document indexing failed.");
            }
        }

        return Result.Ok();
    }

    internal static IReadOnlyList<string> ChunkText(string text)
    {
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (trimmed.Length < MinChunkChars)
                continue;

            if (trimmed.Length <= MaxChunkChars)
            {
                chunks.Add(trimmed);
                continue;
            }

            // Split oversized paragraphs at sentence boundaries
            var start = 0;
            while (start < trimmed.Length)
            {
                var end = Math.Min(start + MaxChunkChars, trimmed.Length);
                chunks.Add(trimmed[start..end]);
                start = end;
            }
        }

        return chunks;
    }
}
