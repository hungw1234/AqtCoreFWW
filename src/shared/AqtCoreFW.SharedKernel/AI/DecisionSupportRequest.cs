namespace AqtCoreFW.SharedKernel.AI;

/// <summary>Input for AI decision support — natural-language question with entity context.</summary>
public sealed record DecisionSupportRequest(
    string Context,
    string Question,
    Guid TenantId);
