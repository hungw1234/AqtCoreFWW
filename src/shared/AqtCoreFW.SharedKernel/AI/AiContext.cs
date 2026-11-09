using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Context passed along every AI request — carries tenant, user, correlation, and classification.
/// Classification drives AiRoutingService data sovereignty routing decision.
/// </summary>
public sealed record AiContext(
    Guid TenantId,
    Guid UserId,
    string CorrelationId,
    ClassificationLevel Classification = ClassificationLevel.Internal);
