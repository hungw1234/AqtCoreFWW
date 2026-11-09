using AqtCoreFW.SharedKernel.AI;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// No-op stub used when Ollama is disabled.
/// Always returns low risk with empty flags — fail-open behaviour.
/// </summary>
public sealed class AiAutoFlaggingServiceStub : IAiAutoFlaggingService
{
    public Task<AutoFlagResult> FlagAsync(string content, Guid tenantId, CancellationToken ct = default)
        => Task.FromResult(new AutoFlagResult("low", [], "Auto-flagging not configured."));
}
