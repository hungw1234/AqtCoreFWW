namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Result of an OCSP (Online Certificate Status Protocol) check against a CA responder.
/// </summary>
public enum OcspCertificateStatus
{
    /// <summary>Certificate is valid and not revoked.</summary>
    Good,

    /// <summary>Certificate has been revoked by the issuing CA.</summary>
    Revoked,

    /// <summary>The responder does not know about the certificate.</summary>
    Unknown,

    /// <summary>
    /// OCSP responder was unreachable (network timeout, DNS failure, etc.).
    /// Handling depends on <see cref="AqtCoreFW.Identity.Domain.Services.OcspMode"/>:
    /// Soft = continue as valid; Hard = treat as invalid.
    /// </summary>
    Unreachable,
}

/// <summary>
/// Abstraction over HTTP OCSP requests — enables mocking in unit tests and
/// swap-in of production implementations (HttpClient-based or stapled).
/// </summary>
public interface IOcspClient
{
    /// <summary>
    /// Checks the revocation status of <paramref name="certRaw"/> against the CA-issued
    /// OCSP responder URL extracted from the certificate AIA extension.
    /// </summary>
    /// <param name="certRaw">DER-encoded end-entity certificate to check.</param>
    /// <param name="issuerCertRaw">DER-encoded issuer certificate (required for OCSP request construction).</param>
    /// <param name="ct">Cancellation token; respects caller-set timeout.</param>
    /// <returns>
    /// <see cref="OcspCertificateStatus.Unreachable"/> on any network/timeout error so callers
    /// apply mode-specific handling without catching exceptions.
    /// </returns>
    Task<OcspCertificateStatus> CheckAsync(byte[] certRaw, byte[] issuerCertRaw, CancellationToken ct);
}
