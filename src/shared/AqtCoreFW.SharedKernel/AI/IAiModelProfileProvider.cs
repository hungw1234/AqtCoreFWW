namespace AqtCoreFW.SharedKernel.AI;

/// <summary>
/// Cross-module contract for retrieving AI model profiles from DB.
/// Monolith: InProcessAiModelProfileProvider (direct DB query).
/// Used by AiModelRouter to select models based on tenant policy + classification.
/// </summary>
public interface IAiModelProfileProvider
{
    Task<AiModelProfileInfo?> GetDefaultProfileAsync(CancellationToken ct = default);
    Task<AiModelProfileInfo?> GetProfileByProviderAsync(string provider, CancellationToken ct = default);
    Task<IReadOnlyList<AiModelProfileInfo>> GetActiveProfilesAsync(CancellationToken ct = default);
}

/// <summary>Id = T1 long PK of AiModelProfile (phase-04b B5a type alignment).</summary>
public sealed record AiModelProfileInfo(
    long Id,
    string Name,
    string Provider,
    string ModelId,
    string? Endpoint,
    int MaxTokens,
    decimal Temperature,
    decimal CostPerToken,
    bool IsDefault);
