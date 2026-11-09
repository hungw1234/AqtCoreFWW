using AqtCoreFW.SharedKernel.AI;
using FluentResults;
using System.Runtime.CompilerServices;

namespace AqtCoreFW.Ai.Infrastructure.Services;

// ── Grouped no-op stubs used when Ollama is disabled (AI:Ollama:Enabled=false) ──
// All return safe defaults or GOV error codes — never throw.

/// <summary>Stub for ILocalAiChatService — Ollama not configured.</summary>
internal sealed class LocalAiChatServiceStub : ILocalAiChatService
{
    public Task<Result<string>> ChatAsync(string prompt, AiContext ctx, CancellationToken ct = default)
        => Task.FromResult(Result.Ok(
            "AI not configured. Set AI:Ollama:Enabled=true and configure Ollama to enable local AI."));
}

/// <summary>Stub for ILocalAiStreamingChatService — Ollama not configured.</summary>
internal sealed class LocalAiStreamingChatServiceStub : ILocalAiStreamingChatService
{
    public async IAsyncEnumerable<StreamingChatToken> StreamAsync(
        AiContext ctx, string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StreamingChatToken(
            "AI not configured. Set AI:Ollama:Enabled=true to enable streaming.",
            IsComplete: true,
            FinishReason: "stub");
        await Task.CompletedTask;
    }
}

/// <summary>Stub for IEmbeddingService — Ollama not configured. Returns zero vector.</summary>
internal sealed class EmbeddingServiceStub : IEmbeddingService
{
    public int VectorDimension => 768;

    public Task<Result<float[]>> EmbedAsync(string text, CancellationToken ct = default)
        => Task.FromResult(Result.Ok(new float[VectorDimension]));
}

/// <summary>Stub for IDocumentIngestionService — silently succeeds without indexing.</summary>
internal sealed class DocumentIngestionServiceStub : IDocumentIngestionService
{
    public Task<Result> IngestAsync(
        Guid fileId, string text, Guid tenantId, CancellationToken ct = default)
        => Task.FromResult(Result.Ok());
}

/// <summary>Stub for IRagService — routes question directly without context retrieval.</summary>
internal sealed class RagServiceStub : IRagService
{
    public Task<Result<string>> QueryAsync(
        string question, AiContext ctx, CancellationToken ct = default)
        => Task.FromResult(Result.Ok(
            "RAG not configured. Set AI:Ollama:Enabled=true and configure Qdrant to enable RAG."));
}

/// <summary>Stub for IAiRoutingService — always returns not-configured message.</summary>
internal sealed class AiRoutingServiceStub : IAiRoutingService
{
    public Task<Result<string>> RouteAsync(
        string prompt,
        AqtCoreFW.SharedKernel.Domain.ClassificationLevel classification,
        AiContext ctx,
        CancellationToken ct = default)
        => Task.FromResult(Result.Ok(
            "AI routing not configured. Set AI:Ollama:Enabled=true to enable AI features."));
}

/// <summary>Stub for ICloudAiStreamingChatService — cloud AI disabled for sovereign compliance.</summary>
internal sealed class CloudAiStreamingChatServiceStub : ICloudAiStreamingChatService
{
    public async IAsyncEnumerable<StreamingChatToken> StreamAsync(
        AiContext ctx, string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StreamingChatToken(
            "GOV_AI_CLOUD_DISABLED: Cloud AI streaming disabled for sovereign compliance.",
            IsComplete: true,
            FinishReason: "sovereign_block");
        await Task.CompletedTask;
    }
}
