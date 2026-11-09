using AqtCoreFW.Ai.Infrastructure.VectorStore;
using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using Microsoft.Extensions.Logging;
using VectorSearchResult = AqtCoreFW.Ai.Infrastructure.VectorStore.VectorSearchResult;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Retrieval-Augmented Generation pipeline.
/// 1. Embed question via IEmbeddingService.
/// 2. Search Qdrant: score_threshold=0.7, limit=5, filter by tenant_id payload.
/// 3. Inject top chunks as context.
/// 4. Route augmented prompt through IAiRoutingService.
/// Qdrant down → degraded mode (empty context, prompt routed without RAG).
/// </summary>
public sealed class RagService(
    IEmbeddingService embeddingService,
    IQdrantVectorStore vectorStore,
    IAiRoutingService routingService,
    ILogger<RagService> logger)
    : IRagService
{
    private const int TopK = 5;
    private const float ScoreThreshold = 0.7f;

    public async Task<Result<string>> QueryAsync(
        string question,
        AiContext ctx,
        CancellationToken ct = default)
    {
        // Step 1: embed question
        var embedResult = await embeddingService.EmbedAsync(question, ct);
        if (embedResult.IsFailed)
        {
            logger.LogWarning("RAG embedding failed — routing without context: {Errors}", embedResult.Errors);
            return await routingService.RouteAsync(question, ctx.Classification, ctx, ct);
        }

        // Step 2: Qdrant semantic search
        var collectionName = $"vectors_{ctx.TenantId:N}";
        var searchResult = await vectorStore.SearchAsync(
            collectionName, embedResult.Value, TopK, ScoreThreshold, ct);

        // Step 3: build augmented prompt
        var prompt = BuildAugmentedPrompt(question, searchResult);

        // Step 4: route through sovereignty layer
        return await routingService.RouteAsync(prompt, ctx.Classification, ctx, ct);
    }

    private static string BuildAugmentedPrompt(
        string question,
        IReadOnlyList<VectorSearchResult> chunks)
    {
        if (chunks.Count == 0)
            return question;

        var context = string.Join("\n\n", chunks.Select(c => c.Text));
        return $"""
            Sử dụng các đoạn văn bản dưới đây để trả lời câu hỏi.
            Nếu không tìm thấy thông tin liên quan, hãy nói rõ điều đó.

            === TÀI LIỆU THAM KHẢO ===
            {context}

            === CÂU HỎI ===
            {question}
            """;
    }
}
