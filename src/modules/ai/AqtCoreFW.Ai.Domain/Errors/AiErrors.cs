using FluentResults;

namespace AqtCoreFW.Ai.Domain.Errors;

/// <summary>
/// AI module error codes — all GOV_AI_XXX prefixed for audit traceability.
/// </summary>
public static class AiErrors
{
    /// <summary>Local AI not configured but Confidential+ data requires local processing.</summary>
    public static Error SovereigntyViolation() =>
        new Error("GOV_AI_001: Data sovereignty violation — classification >= Confidential requires local AI. Configure Ollama or downgrade classification.");

    /// <summary>Ollama is configured but circuit breaker is open (service unavailable).</summary>
    public static Error OllamaUnavailable() =>
        new Error("GOV_AI_002: Ollama service unavailable — circuit breaker open. Retry after 60s.");

    /// <summary>Qdrant vector store unavailable during document ingestion.</summary>
    public static Error VectorStoreUnavailable() =>
        new Error("GOV_AI_003: Vector store (Qdrant) unavailable — document indexing failed.");

    /// <summary>Ollama inference queue is full (capacity 50) — reject immediately.</summary>
    public static Error InferenceQueueFull() =>
        new Error("GOV_AI_004: Ollama inference queue full (capacity=50). Too many concurrent AI requests.");

    /// <summary>Cloud AI disabled for sovereign compliance — use local AI.</summary>
    public static Error CloudAiDisabled() =>
        new Error("GOV_AI_CLOUD_DISABLED: Cloud AI disabled for sovereign compliance. Register ICloudAiChatService to enable.");

    /// <summary>Feature not implemented in this framework version.</summary>
    public static Error NotImplemented(string feature) =>
        new Error($"GOV_AI_NOT_IMPL: {feature} is not implemented. Register a concrete service implementation.");

    /// <summary>Session not found.</summary>
    public static Error SessionNotFound(Guid sessionId) =>
        new Error($"GOV_AI_SESSION_NOT_FOUND: AI session {sessionId} not found.");
}
