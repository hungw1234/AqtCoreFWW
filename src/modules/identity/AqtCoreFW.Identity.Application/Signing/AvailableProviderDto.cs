using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Read-model DTO for an available IDigitalSignatureProvider.
/// Returned by GET api/v1/admin/security/signature-config/available-providers.
/// </summary>
public sealed record AvailableProviderDto(
    string ProviderKey,
    string DisplayName,
    SignatureProviderCapabilities Capabilities);
