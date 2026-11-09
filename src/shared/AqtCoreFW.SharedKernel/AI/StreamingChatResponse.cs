namespace AqtCoreFW.SharedKernel.AI;

/// <summary>SSE streaming token for AI chat responses.</summary>
public sealed record StreamingChatToken(
    string Token,
    bool IsComplete,
    string? FinishReason = null);
