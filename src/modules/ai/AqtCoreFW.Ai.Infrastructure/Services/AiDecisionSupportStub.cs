using AqtCoreFW.SharedKernel.AI;
using FluentResults;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Stub — returns empty recommendation with Confidence=0 without throwing.
/// Replace with real decision model per project.
/// </summary>
public sealed class AiDecisionSupportStub : IAiDecisionSupport
{
    public Task<Result<DecisionSupportResponse>> SuggestAsync(
        DecisionSupportRequest request, CancellationToken ct = default)
        => Task.FromResult(Result.Ok(new DecisionSupportResponse(
            Recommendation: string.Empty,
            Confidence: 0.0,
            Rationale: "AI decision support not configured. Register IAiDecisionSupport implementation.")));
}
