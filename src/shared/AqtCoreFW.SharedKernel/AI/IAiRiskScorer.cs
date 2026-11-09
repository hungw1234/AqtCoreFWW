using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// AI-powered risk scoring for entities (cases, submissions, users).
/// Returns 0-100 score with level and reasoning.
/// Stub default: Score=0, Level=Low (safe fallback).
/// </summary>
/// <remarks>
/// DEFERRED IMPLEMENTATION — interface kept as port per platform strategy.
/// Concrete adapter will be wired when a concrete business case arrives.
/// See docs/system-architecture.md → "AI Module — Implementation Strategy".
/// </remarks>
public interface IAiRiskScorer
{
    Task<Result<RiskScore>> ScoreAsync(RiskScoringRequest request, CancellationToken ct = default);
}
