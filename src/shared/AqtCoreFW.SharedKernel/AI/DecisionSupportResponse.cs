namespace AqtCoreFW.SharedKernel.AI;

/// <summary>AI decision support recommendation with confidence and rationale.</summary>
public sealed record DecisionSupportResponse(
    string Recommendation,
    double Confidence,
    string Rationale);
