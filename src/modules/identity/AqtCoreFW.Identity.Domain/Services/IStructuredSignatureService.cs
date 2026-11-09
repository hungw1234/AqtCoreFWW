namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Tier-2 structured document verification — XMLDSig + JWS.
///
/// <para>Implementation target (W2): <c>StructuredSignatureService</c> in Identity.Infrastructure.
/// <list type="bullet">
///   <item>XML: uses <c>System.Security.Cryptography.Xml.SignedXml</c> to validate XMLDSig signature;
///     signer certificate extracted from <c>KeyInfo/X509Data</c> element; chain validated via
///     <c>ICertificateChainValidator</c> with the supplied OCSP mode.</item>
///   <item>JWS: compact serialization parsed manually; signer certificate extracted from <c>x5c</c>
///     header claim (first element = leaf cert DER, remaining = intermediates); chain validated
///     via <c>ICertificateChainValidator</c>.</item>
/// </list>
/// </para>
///
/// <para>Both methods return a unified <see cref="VerifyResult"/> — callers do not need to know
/// which structured format was used; the format is determined by the incoming bytes/string.</para>
/// </summary>
public interface IStructuredSignatureService
{
    /// <summary>
    /// Verify an XMLDSig-signed XML document.
    /// The signing certificate is extracted from the <c>KeyInfo/X509Data</c> node inside the signed document.
    /// Certificate chain validation uses <paramref name="ocspMode"/> to control OCSP strictness.
    /// </summary>
    /// <param name="xmlDsigBytes">Raw bytes of the XMLDSig document (UTF-8 or UTF-16 XML).</param>
    /// <param name="ocspMode">OCSP validation mode — <see cref="OcspMode.Soft"/> treats OCSP failures
    /// as non-blocking warnings; <see cref="OcspMode.Hard"/> rejects on OCSP error.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="VerifyResult"/> with chain details populated.</returns>
    Task<VerifyResult> VerifyXmlDsigAsync(byte[] xmlDsigBytes, OcspMode ocspMode, CancellationToken ct = default);

    /// <summary>
    /// Verify a JWS compact serialization (three Base64Url segments separated by dots).
    /// The signing certificate is extracted from the <c>x5c</c> JWS header (leaf + chain).
    /// Certificate chain validation uses <paramref name="ocspMode"/> to control OCSP strictness.
    /// </summary>
    /// <param name="jwsCompact">JWS compact serialization string
    /// (<c>header.payload.signature</c> Base64Url-encoded).</param>
    /// <param name="ocspMode">OCSP validation mode.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="VerifyResult"/> with chain details populated.</returns>
    Task<VerifyResult> VerifyJwsAsync(string jwsCompact, OcspMode ocspMode, CancellationToken ct = default);
}
