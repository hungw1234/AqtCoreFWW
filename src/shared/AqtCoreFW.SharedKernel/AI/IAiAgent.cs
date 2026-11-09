using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Agentic AI contract — stub only in framework.
/// Concrete ReAct/tool-use agent is project-specific.
/// Stub returns AgentResponse(Success=false) without throwing.
/// </summary>
public interface IAiAgent
{
    Task<Result<AgentResponse>> ExecuteAsync(AgentTask task, CancellationToken ct = default);
}
