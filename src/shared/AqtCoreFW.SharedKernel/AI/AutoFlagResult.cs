namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Result of an AI content classification / auto-flagging pass.
/// RiskLevel: "low" | "medium" | "high" | "critical"
/// DetectedFlags: subset of pii, sensitive_data, policy_violation, financial, legal
/// </summary>
public sealed record AutoFlagResult(
    string RiskLevel,
    string[] DetectedFlags,
    string Explanation);
