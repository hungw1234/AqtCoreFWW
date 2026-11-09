namespace AqtCoreFW.Ai.Infrastructure.VectorStore;

/// <summary>
/// Qdrant connection options — bound from config section "AI:Qdrant".
/// ApiKey: optional (required for Qdrant Cloud; omit for self-hosted dev).
/// </summary>
public sealed class QdrantOptions
{
    public string Endpoint { get; set; } = "http://localhost:6333";
    public string? ApiKey { get; set; }
    public int DefaultVectorSize { get; set; } = 768; // nomic-embed-text dims

    // Explicit HTTP timeout for Qdrant calls — avoids inheriting the implicit .NET default (100s),
    // which would let a stalled vector op hang a request far longer than a RAG path should tolerate.
    public int TimeoutSeconds { get; set; } = 30;
}
