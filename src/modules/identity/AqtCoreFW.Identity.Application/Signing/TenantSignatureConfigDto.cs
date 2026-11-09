using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Read-model DTO for TenantSignatureConfig (T1 entity — singleton per tenant).
/// Addressed by TenantId (no separate public/internal Id split needed at the API surface).
/// RowVersion: base64-encoded for JSON transport; used for optimistic concurrency on PUT.
/// </summary>
public sealed record TenantSignatureConfigDto(
    Guid TenantId,
    string ProviderKey,
    string? ApiCredentialsRef,
    string AllowedCaRootThumbprints,
    OcspMode OcspMode,
    bool IsEnabled,
    SignatureFlowMode SignFlowMode,
    string RowVersion,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
