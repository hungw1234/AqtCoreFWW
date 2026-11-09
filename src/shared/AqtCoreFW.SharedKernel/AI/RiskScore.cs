namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Risk assessment result — 0-100 score with level and reasons.
/// Stub default: Score=0, Level=Low (safe fallback per phase requirements).
/// </summary>
public sealed record RiskScore(
    int Score,
    RiskLevel Level,
    IReadOnlyList<string> Reasons);

public enum RiskLevel { Low, Medium, High, Critical }
