namespace AqtCoreFW.SharedKernel.AI;

/// <summary>Task submitted to an AI agent for autonomous execution.</summary>
public sealed record AgentTask(
    string TaskType,
    IReadOnlyDictionary<string, string> Parameters,
    Guid TenantId);
