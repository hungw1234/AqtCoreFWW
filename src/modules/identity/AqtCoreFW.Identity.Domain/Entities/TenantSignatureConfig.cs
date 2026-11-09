using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Per-tenant digital signature provider configuration (QĐ 742 mục 5.1).
/// One row per tenant — admin selects which VN CA provider to use and supplies Vault credential reference.
///
/// Tier: T1 — admin configuration entity, addressed by singleton GET/PUT per tenant.
/// Schema: [identity] (via IdentityDbContext default schema).
///
/// OcspMode: D-V3 hybrid OCSP control — Soft = treat OCSP failure as warn-only; Hard = fail verification on OCSP error.
/// AllowedCaRootThumbprints: CSV of SHA-1 thumbprints pinning VN CA root certificates.
/// ApiCredentialsRef: Vault key path (e.g. "aqt/signing/viettel-mysign/api-credentials") resolved at runtime by IKeyResolver.
/// ProviderKey: matches IDigitalSignatureProvider.ProviderKey — valid values after D19: "usb-vgca", "remote-viettel-mysign".
/// RowVersion: optimistic concurrency token — admin UI may hold stale config across tab sessions.
///
/// <para><b>D19 deprecation (2026-05-11):</b> the provider key for VNPT SmartCA sp769 is deprecated
/// in favour of Viettel Mysign. New writes using the deprecated key are rejected at the Application
/// validator layer (see <c>UpdateTenantSignatureConfigCommandValidator</c>). Existing rows with the
/// deprecated key remain readable until the tenant admin reconfigures them via the admin UI.
/// W4 (Viettel Mysign provider implementation) must be shipped before enforcing migration of active rows.</para>
/// </summary>
public class TenantSignatureConfig : AuditableEntity, ITenantScoped
{
    /// <summary>Tenant this config belongs to. Unique — one config per tenant.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>
    /// Matches <see cref="IDigitalSignatureProvider.ProviderKey"/>.
    /// Valid values after D19 (2026-05-11): "usb-vgca", "remote-viettel-mysign".
    /// Resolved by <see cref="ISignatureProviderResolver"/> to find the active provider implementation.
    /// The VNPT SmartCA sp769 provider key is deprecated — new writes rejected at the Application
    /// validator layer; existing rows remain read-only until admin reconfigures.
    /// </summary>
    public string ProviderKey { get; private set; } = string.Empty;

    /// <summary>
    /// Vault key reference for provider API credentials (e.g. "aqt/signing/viettel-mysign/credentials").
    /// Null when provider does not require server-side API authentication (e.g. USB token verify-only mode).
    /// </summary>
    public string? ApiCredentialsRef { get; private set; }

    /// <summary>
    /// Comma-separated SHA-1 thumbprints of trusted VN CA root certificates.
    /// Used by ChainValidator to pin allowed roots — prevents accepting signatures from untrusted CAs.
    /// Max 2048 chars accommodates up to ~40 root thumbprints (40-char SHA-1 each + separators).
    /// </summary>
    public string AllowedCaRootThumbprints { get; private set; } = string.Empty;

    /// <summary>
    /// D-V3 hybrid OCSP mode: Soft (default) = treat OCSP failure as non-blocking warn;
    /// Hard = reject verification when OCSP is unreachable or returns error.
    /// </summary>
    public OcspMode OcspMode { get; private set; } = OcspMode.Soft;

    /// <summary>
    /// When false the signing integration is inactive — Sign/Verify endpoints return 503.
    /// Default false until tenant admin explicitly enables after configuring credentials.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// How the server-side remote signature flow is driven for this tenant: Async (default, non-blocking poll)
    /// or Sync (block on the mobile OTP in-request). Only affects server-side remote providers (Viettel Mysign
    /// PAdES); ignored by client-side/USB providers. See <see cref="SignatureFlowMode"/> for the trade-offs.
    /// </summary>
    public SignatureFlowMode SignFlowMode { get; private set; } = SignatureFlowMode.Async;

    /// <summary>EF optimistic concurrency token.</summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private TenantSignatureConfig() { }

    /// <summary>
    /// Factory — call from command handler. CreatedBy = acting admin UserId.
    ///
    /// <para><b>D19 ProviderKey contract:</b> valid values are "usb-vgca" and "remote-viettel-mysign".
    /// The deprecated VNPT SmartCA provider key is rejected; throw is at the Application validator
    /// layer to preserve D6 (no hardcoded vendor strings in Domain source).
    /// See <c>UpdateTenantSignatureConfigCommandValidator</c> for the enforcement point.</para>
    /// </summary>
    public static TenantSignatureConfig Create(
        Guid tenantId,
        string providerKey,
        string? apiCredentialsRef,
        string allowedCaRootThumbprints,
        OcspMode ocspMode,
        bool isEnabled,
        SignatureFlowMode signFlowMode,
        Guid createdBy)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(providerKey))
            throw new ArgumentException("ProviderKey must not be empty.", nameof(providerKey));
        if (providerKey.Length > 64)
            throw new ArgumentException("ProviderKey must not exceed 64 characters.", nameof(providerKey));
        if (apiCredentialsRef is not null && apiCredentialsRef.Length > 256)
            throw new ArgumentException("ApiCredentialsRef must not exceed 256 characters.", nameof(apiCredentialsRef));
        if (allowedCaRootThumbprints.Length > 2048)
            throw new ArgumentException("AllowedCaRootThumbprints must not exceed 2048 characters.", nameof(allowedCaRootThumbprints));

        var config = new TenantSignatureConfig
        {
            TenantId                  = tenantId,
            ProviderKey               = providerKey.Trim(),
            ApiCredentialsRef         = apiCredentialsRef?.Trim(),
            AllowedCaRootThumbprints  = allowedCaRootThumbprints.Trim(),
            OcspMode                  = ocspMode,
            IsEnabled                 = isEnabled,
            SignFlowMode              = signFlowMode,
        };
        config.SetAuditCreate(createdBy);
        return config;
    }

    /// <summary>
    /// Update settings — call from command handler. ModifiedBy = acting admin UserId.
    ///
    /// <para><b>D19 ProviderKey contract:</b> same as <see cref="Create"/> — deprecated VNPT SmartCA
    /// provider key is rejected at the Application validator layer before this method is called.</para>
    /// </summary>
    public void Update(
        string providerKey,
        string? apiCredentialsRef,
        string allowedCaRootThumbprints,
        OcspMode ocspMode,
        bool isEnabled,
        SignatureFlowMode signFlowMode,
        Guid modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
            throw new ArgumentException("ProviderKey must not be empty.", nameof(providerKey));
        if (providerKey.Length > 64)
            throw new ArgumentException("ProviderKey must not exceed 64 characters.", nameof(providerKey));
        if (apiCredentialsRef is not null && apiCredentialsRef.Length > 256)
            throw new ArgumentException("ApiCredentialsRef must not exceed 256 characters.", nameof(apiCredentialsRef));
        if (allowedCaRootThumbprints.Length > 2048)
            throw new ArgumentException("AllowedCaRootThumbprints must not exceed 2048 characters.", nameof(allowedCaRootThumbprints));

        ProviderKey              = providerKey.Trim();
        ApiCredentialsRef        = apiCredentialsRef?.Trim();
        AllowedCaRootThumbprints = allowedCaRootThumbprints.Trim();
        OcspMode                 = ocspMode;
        IsEnabled                = isEnabled;
        SignFlowMode             = signFlowMode;
        SetAuditUpdate(modifiedBy);
    }
}
