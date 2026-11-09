using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Minimal, non-sensitive signing-provider read model for the form-fill signing modal.
/// Returned by GET api/v1/digital-signature/provider-info (caller's own tenant, JWT-scoped).
///
/// <para><see cref="Capabilities"/> serializes as a comma-separated string of [Flags] enum names
/// via the globally-registered JsonStringEnumConverter (same wire shape as
/// <see cref="AvailableProviderDto.Capabilities"/>).</para>
/// </summary>
public sealed record SigningProviderInfoDto(
    bool IsEnabled,
    string? ProviderKey,
    SignatureProviderCapabilities Capabilities);
