using FluentResults;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// AI decision support — suggests next action for gov officers based on entity context.
/// Stub default: returns empty recommendation with Confidence=0.
/// </summary>
/// <remarks>
/// DEFERRED IMPLEMENTATION — interface kept as port per platform strategy.
/// Concrete adapter will be wired when a concrete business case arrives.
/// See docs/system-architecture.md → "AI Module — Implementation Strategy".
/// </remarks>
public interface IAiDecisionSupport
{
    Task<Result<DecisionSupportResponse>> SuggestAsync(
        DecisionSupportRequest request,
        CancellationToken ct = default);
}
