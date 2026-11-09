using Org.BouncyCastle.Asn1;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// Splices a detached CMS (signed attributes + raw RSA signature + signer cert chain) into a prepared
/// placeholder PDF's <c>/Contents</c> gap, producing the final signed PDF. Shared by BOTH remote flows:
/// <see cref="PadesTransactionInitiator"/> (sync, in-request) and <see cref="PadesCompletionService"/>
/// (async-poll completion) — the assembly is identical, only WHEN the raw signature arrives differs.
///
/// <para>The signed-attributes SET is re-parsed from its canonical DER so the messageDigest the signature was
/// computed over is preserved byte-for-byte — the splice cannot invalidate the signature.</para>
/// </summary>
internal static class PadesPdfAssembler
{
    private static readonly PadesCmsBuilder CmsBuilder = new();
    private static readonly PdfIncrementalSignatureWriter Writer = new();

    /// <summary>
    /// Reconstructs the CMS from the persisted/in-memory signed-attributes DER + signer cert DER + the raw
    /// signature, then embeds it into the placeholder at the recorded <c>/Contents</c> gap.
    /// </summary>
    /// <param name="signedAttributesDer">Canonical DER of the signed attributes SET (the bytes that were hashed+signed).</param>
    /// <param name="signerCertDer">DER of the signer leaf certificate.</param>
    /// <param name="rawSignature">Raw RSA PKCS#1 v1.5 signature bytes from Mysign.</param>
    /// <param name="placeholderPdf">The prepared placeholder PDF bytes (ByteRange laid out, /Contents zeroed).</param>
    /// <param name="contentsGapStart">Byte offset of the first /Contents hex digit.</param>
    /// <param name="contentsHexLength">Width (hex chars) of the /Contents placeholder gap.</param>
    public static byte[] Assemble(
        byte[] signedAttributesDer,
        byte[] signerCertDer,
        byte[] rawSignature,
        byte[] placeholderPdf,
        int contentsGapStart,
        int contentsHexLength)
    {
        // SignInput (SHA-256 of the DER) is the hash that was ALREADY signed; assembly never re-signs, and
        // AssembleDetachedCms reads only .Set + .Der — so SignInput is a dead field here. Pass [] to make that
        // explicit (a non-empty value could mislead a reader into thinking assembly re-hashes).
        var attrs = new PadesSignedAttributes(
            Asn1Set.GetInstance(Asn1Object.FromByteArray(signedAttributesDer)), signedAttributesDer, SignInput: []);

        var signerCert = new BcX509.X509CertificateParser().ReadCertificate(signerCertDer);
        byte[] cms = CmsBuilder.AssembleDetachedCms(attrs, rawSignature, [signerCert]);

        // EmbedCms only needs the placeholder bytes + the /Contents gap location (offset + width).
        var prep = new PdfSigningPreparation(
            placeholderPdf, contentsGapStart, contentsHexLength, ByteRange: [], DocumentDigest: []);
        return Writer.EmbedCms(prep, cms);
    }
}
