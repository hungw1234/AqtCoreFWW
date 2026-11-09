using AqtCoreFW.SharedKernel.AI;
using FluentResults;

namespace AqtCoreFW.Ai.Infrastructure.Services;

/// <summary>
/// Stub — returns Score=0, Level=Low (safe default) without throwing.
/// Replace with real risk model per project.
/// </summary>
public sealed class AiRiskScorerStub : IAiRiskScorer
{
    public Task<Result<RiskScore>> ScoreAsync(RiskScoringRequest request, CancellationToken ct = default)
        => Task.FromResult(Result.Ok(new RiskScore(
            Score: 0,
            Level: RiskLevel.Low,
            Reasons: ["AI risk scoring not configured — safe default returned."])));
}
