using AqtCoreFW.Identity.Domain.Services;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Validates a certificate chain against the VN CA trust anchors stored in <see cref="VnCaRootStore"/>,
/// performing per-link signature verification and optional OCSP revocation checking.
///
/// <para>
/// Injected by W4 (VGCA USB Token provider) and W5 (VNPT Remote provider) to share a
/// single validation pipeline. W6 wires the concrete <see cref="CertificateChainValidator"/>
/// implementation into the DI container.
/// </para>
/// </summary>
public interface ICertificateChainValidator
{
    /// <summary>
    /// Validates the certificate chain rooted at a VN CA trust anchor.
    /// </summary>
    /// <param name="certRaw">DER-encoded end-entity certificate to validate.</param>
    /// <param name="intermediates">
    /// Optional DER-encoded intermediate CA certificates. Supply in any order;
    /// the validator sorts them into the correct chain path automatically.
    /// </param>
    /// <param name="mode">
    /// Controls handling of OCSP responder unreachability:
    /// <see cref="OcspMode.Soft"/> treats unreachable as non-fatal (warn + continue);
    /// <see cref="OcspMode.Hard"/> treats unreachable as a validation failure.
    /// Revoked status is always fatal regardless of mode.
    /// </param>
    /// <param name="ct">Cancellation token; propagated to OCSP HTTP calls.</param>
    /// <returns>
    /// <see cref="ChainValidationResult"/> with <c>IsValid=true</c> only when the full chain
    /// verifies, the root thumbprint is pinned in <see cref="VnCaRootStore"/>, the end-entity
    /// certificate is within its validity window, and OCSP (if reachable) returns Good.
    /// </returns>
    Task<ChainValidationResult> ValidateAsync(
        byte[] certRaw,
        IEnumerable<byte[]>? intermediates,
        OcspMode mode,
        CancellationToken ct);
}
