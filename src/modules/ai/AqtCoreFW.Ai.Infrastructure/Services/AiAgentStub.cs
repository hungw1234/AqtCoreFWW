using AqtCoreFW.SharedKernel.AI;
using FluentResults;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Stub — returns Success=false without throwing.
/// Concrete ReAct/tool-use agent is project-specific — not bundled in framework.
/// </summary>
public sealed class AiAgentStub : IAiAgent
{
    public Task<Result<AgentResponse>> ExecuteAsync(AgentTask task, CancellationToken ct = default)
        => Task.FromResult(Result.Ok(new AgentResponse(
            Output: string.Empty,
            Metadata: new Dictionary<string, string>(),
            Success: false)));
}
