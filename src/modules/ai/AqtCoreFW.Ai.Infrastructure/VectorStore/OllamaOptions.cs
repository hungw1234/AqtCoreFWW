namespace AqtCoreFW.Ai.Infrastructure.VectorStore;

/// <summary>
/// Ollama connection options — bound from config section "AI:Ollama".
/// CPU-only defaults: qwen2.5:7b (chat, better Vietnamese), nomic-embed-text (embedding).
/// </summary>
public sealed class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "qwen2.5:7b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public bool Enabled { get; set; } = false; // false = NoopAiService (no GPU/Ollama dependency)
}
