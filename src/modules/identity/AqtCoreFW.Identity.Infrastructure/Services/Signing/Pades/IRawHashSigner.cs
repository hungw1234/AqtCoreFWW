using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// The two-call hash-signer seam that isolates the crypto provider from the PAdES assembly. The CMS builder and
/// the PDF incremental-update writer are provider-agnostic; only this seam changes between the spike's local RSA
/// key and the production Viettel Mysign hash-signer.
///
/// <para>The split is deliberate so swapping providers is a one-implementation change: the signer chain is fetched
/// up front (Mysign: from <c>certificates/info</c>; local: the generated self-signed cert) because the
/// signing-certificate-v2 attribute hashes the signer leaf, so the cert must be known BEFORE the signed
/// attributes are built. <see cref="Sign"/> then receives the <c>SHA-256(DER(signedAttributes))</c> hash —
/// NOT the document hash. Feeding the document hash here is the canonical PAdES false-green: self-tests pass,
/// Adobe reports the signature invalid. The Mysign impl maps this exactly to
/// <c>SignHashAsync(hashBytes, hashAlgo=SHA256-OID, signAlgo=RSA-OID)</c>.</para>
/// </summary>
public interface IRawHashSigner
{
    /// <summary>
    /// Signer certificate chain: <c>[0]</c> = the signer leaf (hashed into signing-certificate-v2 and embedded in
    /// the CMS certificate bag), remaining entries = intermediates in chain-building order. Never empty.
    /// </summary>
    IReadOnlyList<BcX509.X509Certificate> SignerChain { get; }

    /// <summary>
    /// Produces the raw RSASSA-PKCS1-v1_5 signature over <paramref name="sha256OfSignedAttributesDer"/>, which MUST
    /// be <c>SHA-256(DER(signedAttributes))</c>. The returned bytes are the CMS <c>SignerInfo.signature</c> value
    /// (the provider performs the DigestInfo wrapping + RSA private-key operation internally, mirroring Mysign's
    /// hash-signer contract).
    /// </summary>
    byte[] Sign(byte[] sha256OfSignedAttributesDer);
}
