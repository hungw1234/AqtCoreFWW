namespace AqtCoreFW.SharedKernel.AI;

/// <summary>Input for AI risk scoring — entity type + contextual factors dictionary.</summary>
public sealed record RiskScoringRequest(
    string EntityType,
    Guid EntityId,
    IReadOnlyDictionary<string, string> Context);
