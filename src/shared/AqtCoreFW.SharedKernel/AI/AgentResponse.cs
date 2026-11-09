namespace AqtCoreFW.SharedKernel.AI;

/// <summary>Result returned by an AI agent after task execution.</summary>
public sealed record AgentResponse(
    string Output,
    IReadOnlyDictionary<string, string> Metadata,
    bool Success);
