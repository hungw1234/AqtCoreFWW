namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for recording PDPL consent.
/// Monolith: InProcessConsentModuleClient (direct repository call).
/// Microservice: gRPC client when Identity module extracted.
/// </summary>
public interface IConsentModuleClient
{
    Task<Guid> RecordConsentAsync(RecordConsentRequest request, CancellationToken ct = default);
}

public record RecordConsentRequest(
    Guid DataSubjectId,
    Guid TenantId,
    string PurposeCode,
    string LegalBasis,
    string DataSubjectType,
    string? EvidenceJson);
