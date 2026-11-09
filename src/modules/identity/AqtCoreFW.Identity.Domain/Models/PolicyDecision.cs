namespace AqtCoreFW.Identity.Domain.Models;

/// <summary>
/// Result of a policy rule evaluation for a given permission + context.
/// IsAllowed=false means access is denied; DenyReason and RuleCode identify which rule triggered.
/// </summary>
public sealed record PolicyDecision(
    bool IsAllowed,
    string? DenyReason = null,
    string? RuleCode = null);
