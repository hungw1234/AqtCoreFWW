using System.Security.Cryptography;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Asn1.Ess;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Cms = Org.BouncyCastle.Asn1.Cms;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// Builds the PAdES-B-B (CAdES-BES, no LTV) signed attributes and assembles the detached CMS/PKCS#7 that is
/// spliced into the PDF signature dictionary's <c>/Contents</c>. This is the "20%" the plan describes —
/// BouncyCastle does the CMS; the PDF incremental-update writer does the hard 80%.
///
/// <para><b>Death-trap #1 lives here.</b> The bytes handed to <see cref="IRawHashSigner.Sign"/> are
/// <c>SHA-256(DER(signedAttributes))</c> (the explicit <c>SET OF</c> per RFC 5652 §5.4), exposed on
/// <see cref="PadesSignedAttributes.SignInput"/> — NEVER the document digest. A regression that "simplifies" to
/// the doc digest produces a green self-test and an Adobe-invalid PDF.</para>
/// </summary>
public sealed class PadesCmsBuilder
{
    /// <summary>
    /// Builds the signed attributes for a PAdES-B-B signature over <paramref name="documentDigest"/>
    /// (the SHA-256 of the bytes covered by <c>/ByteRange</c>). Attributes: contentType (id-data),
    /// signingTime, messageDigest, and the mandatory ESS signing-certificate-v2 (SHA-256 of the signer leaf).
    /// </summary>
    public PadesSignedAttributes BuildSignedAttributes(
        byte[] documentDigest, BcX509.X509Certificate signerCert, DateTime signingTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(documentDigest);
        ArgumentNullException.ThrowIfNull(signerCert);

        var contentType = new Org.BouncyCastle.Asn1.Cms.Attribute(
            CmsAttributes.ContentType, new DerSet(PkcsObjectIdentifiers.Data));

        var signingTime = new Org.BouncyCastle.Asn1.Cms.Attribute(
            CmsAttributes.SigningTime,
            new DerSet(new Org.BouncyCastle.Asn1.Cms.Time(signingTimeUtc.ToUniversalTime())));

        var messageDigest = new Org.BouncyCastle.Asn1.Cms.Attribute(
            CmsAttributes.MessageDigest, new DerSet(new DerOctetString(documentDigest)));

        // ESS signing-certificate-v2 (RFC 5035): EssCertIDv2 default hash algorithm is SHA-256, so the single-arg
        // ctor over SHA-256(certDer) is the canonical PAdES form. This binds the signature to THIS signer cert.
        byte[] certHash = SHA256.HashData(signerCert.GetEncoded());
        var signingCertV2 = new Org.BouncyCastle.Asn1.Cms.Attribute(
            PkcsObjectIdentifiers.IdAASigningCertificateV2,
            new DerSet(new SigningCertificateV2(new[] { new EssCertIDv2(certHash) })));

        // DerSet canonicalises (DER SET OF sort). GetEncoded(DER) yields the EXPLICIT 0x31 SET OF tagging that is
        // the hash input per RFC 5652 §5.4 — distinct from the [0] IMPLICIT tagging used inside SignerInfo.
        var attrSet = new DerSet(contentType, signingTime, messageDigest, signingCertV2);
        byte[] der = attrSet.GetEncoded(Asn1Encodable.Der);
        byte[] signInput = SHA256.HashData(der);

        return new PadesSignedAttributes(attrSet, der, signInput);
    }

    /// <summary>
    /// Assembles the detached CMS SignedData from the signed attributes, the raw RSA signature over
    /// <see cref="PadesSignedAttributes.SignInput"/>, and the signer chain (<c>[0]</c>=leaf). Detached =
    /// encapsulated content absent; digestAlgorithm = SHA-256; signatureAlgorithm = rsaEncryption.
    /// </summary>
    public byte[] AssembleDetachedCms(
        PadesSignedAttributes signedAttributes,
        byte[] rawRsaSignature,
        IReadOnlyList<BcX509.X509Certificate> chain)
    {
        ArgumentNullException.ThrowIfNull(signedAttributes);
        ArgumentNullException.ThrowIfNull(rawRsaSignature);
        if (chain is null || chain.Count == 0)
            throw new ArgumentException("Signer chain must contain at least the signer leaf.", nameof(chain));

        var signerLeaf = chain[0];
        var digestAlg = new AlgorithmIdentifier(NistObjectIdentifiers.IdSha256, DerNull.Instance);
        var signatureAlg = new AlgorithmIdentifier(PkcsObjectIdentifiers.RsaEncryption, DerNull.Instance);

        var sid = new SignerIdentifier(
            new Cms.IssuerAndSerialNumber(signerLeaf.IssuerDN, signerLeaf.SerialNumber));

        // authenticatedAttributes: BC re-tags the SET as [0] IMPLICIT inside the SignerInfo SEQUENCE; the signature
        // was computed over the explicit SET OF (SignInput) — the RFC 5652 split, handled correctly here.
        var signerInfo = new Cms.SignerInfo(
            sid, digestAlg, signedAttributes.Set, signatureAlg,
            new DerOctetString(rawRsaSignature), unauthenticatedAttributes: null);

        var certVector = new Asn1EncodableVector();
        foreach (var cert in chain)
            certVector.Add(cert.CertificateStructure);

        // Detached: ContentInfo carries id-data with no content octets (the PDF bytes are external).
        var encapContent = new Cms.ContentInfo(PkcsObjectIdentifiers.Data, null);

        var signedData = new Cms.SignedData(
            new DerSet(digestAlg),
            encapContent,
            new DerSet(certVector),
            crls: null,
            new DerSet(signerInfo));

        var outer = new Cms.ContentInfo(PkcsObjectIdentifiers.SignedData, signedData);
        return outer.GetEncoded(Asn1Encodable.Der);
    }
}

/// <summary>
/// The signed-attributes triple. <see cref="SignInput"/> is what the hash-signer signs; <see cref="Der"/> is the
/// explicit SET OF encoding (<see cref="SignInput"/> = its SHA-256); <see cref="Set"/> goes into the SignerInfo.
/// </summary>
public sealed record PadesSignedAttributes(Asn1Set Set, byte[] Der, byte[] SignInput);
