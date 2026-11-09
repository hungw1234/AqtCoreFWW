using System.Security.Cryptography;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using FluentAssertions;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.X509;
using Xunit;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Pins the PAdES CMS "20%": the signed-attributes shape and the highest-value regression in the whole plan —
/// <b>death-trap #1</b>: the bytes handed to the hash-signer are <c>SHA-256(DER(signedAttributes))</c>, never the
/// document digest. A future "simplification" to the doc digest produces a green self-test and an Adobe-invalid
/// PDF; this test fails first. Also proves the assembled detached CMS verifies end-to-end with BouncyCastle.
/// </summary>
public sealed class PadesCmsBuilderTests
{
    private const string SigningCertV2Oid = "1.2.840.113549.1.9.16.2.47";

    /// <summary>Records the exact bytes the CMS flow hands to the signer (the death-trap #1 witness).</summary>
    private sealed class RecordingHashSigner(IRawHashSigner inner) : IRawHashSigner
    {
        public byte[]? Captured { get; private set; }
        public IReadOnlyList<BcX509.X509Certificate> SignerChain => inner.SignerChain;
        public byte[] Sign(byte[] sha256OfSignedAttributesDer)
        {
            Captured = (byte[])sha256OfSignedAttributesDer.Clone();
            return inner.Sign(sha256OfSignedAttributesDer);
        }
    }

    [Fact]
    public void SignInput_IsHashOfSignedAttributesDer_NotDocumentDigest()
    {
        var builder = new PadesCmsBuilder();
        var signer = new LocalRsaHashSigner();
        byte[] content = RandomNumberGenerator.GetBytes(4096);
        byte[] documentDigest = SHA256.HashData(content);

        var attrs = builder.BuildSignedAttributes(documentDigest, signer.SignerChain[0], DateTime.UtcNow);

        // The witness: SignInput == SHA-256(DER(signedAttrs)) and is provably NOT the document hash.
        attrs.SignInput.Should().Equal(SHA256.HashData(attrs.Der),
            because: "the signed input is the hash of the signedAttributes DER (RFC 5652 §5.4)");
        attrs.SignInput.Should().NotEqual(documentDigest,
            because: "death-trap #1: signing the document digest yields an Adobe-invalid signature");
        attrs.SignInput.Should().NotEqual(SHA256.HashData(content));
    }

    [Fact]
    public void BuildAndAssemble_HandsSignedAttrsHashToSigner()
    {
        var builder = new PadesCmsBuilder();
        var recording = new RecordingHashSigner(new LocalRsaHashSigner());
        byte[] documentDigest = SHA256.HashData("doc"u8.ToArray());

        var attrs = builder.BuildSignedAttributes(documentDigest, recording.SignerChain[0], DateTime.UtcNow);
        byte[] rawSig = recording.Sign(attrs.SignInput);
        _ = builder.AssembleDetachedCms(attrs, rawSig, recording.SignerChain);

        recording.Captured.Should().NotBeNull();
        recording.Captured!.Should().Equal(attrs.SignInput,
            because: "the hash-signer must receive the signedAttributes hash, not the document digest");
    }

    [Fact]
    public void SignedAttributes_CarryAllFourMandatoryAttributes_WithCorrectMessageDigest()
    {
        var builder = new PadesCmsBuilder();
        var signer = new LocalRsaHashSigner();
        byte[] documentDigest = SHA256.HashData("doc"u8.ToArray());

        var attrs = builder.BuildSignedAttributes(documentDigest, signer.SignerChain[0], DateTime.UtcNow);

        // ASN.1-parse the attribute set (not a substring grep) so a missing/garbled attribute or a WRONG
        // messageDigest is caught here, independent of the e2e verify test (defends the recurring false-green gap).
        var byOid = new Dictionary<string, Org.BouncyCastle.Asn1.Cms.Attribute>();
        foreach (var enc in attrs.Set)
        {
            var attr = Org.BouncyCastle.Asn1.Cms.Attribute.GetInstance(enc);
            byOid[attr.AttrType.Id] = attr;
        }

        byOid.Keys.Should().Contain(new[]
        {
            "1.2.840.113549.1.9.3",   // contentType
            "1.2.840.113549.1.9.4",   // messageDigest
            "1.2.840.113549.1.9.5",   // signingTime
            SigningCertV2Oid,          // ESS signing-certificate-v2 (the most commonly missed)
        });

        // contentType must be id-data (PKCS#7 data), matching the detached encapContentInfo.
        var contentType = (Org.BouncyCastle.Asn1.DerObjectIdentifier)byOid["1.2.840.113549.1.9.3"].AttrValues[0];
        contentType.Id.Should().Be("1.2.840.113549.1.7.1");

        // messageDigest octets MUST equal the document digest — a wrong value yields an Adobe-invalid signature.
        var md = (Org.BouncyCastle.Asn1.Asn1OctetString)byOid["1.2.840.113549.1.9.4"].AttrValues[0];
        md.GetOctets().Should().Equal(documentDigest, because: "messageDigest binds the signature to the document bytes");
    }

    [Fact]
    public void AssembledCms_VerifiesOverContent_WithEmbeddedCert()
    {
        var builder = new PadesCmsBuilder();
        var signer = new LocalRsaHashSigner();
        byte[] content = RandomNumberGenerator.GetBytes(2048);
        byte[] documentDigest = SHA256.HashData(content);

        var attrs = builder.BuildSignedAttributes(documentDigest, signer.SignerChain[0], DateTime.UtcNow);
        byte[] rawSig = signer.Sign(attrs.SignInput);
        byte[] cms = builder.AssembleDetachedCms(attrs, rawSig, signer.SignerChain);

        // Detached verify: BC recomputes the digest over `content`, checks it equals the messageDigest attribute,
        // then verifies the RSA signature over the signedAttributes with the embedded cert. Proves the CMS is valid.
        var signedData = new CmsSignedData(new CmsProcessableByteArray(content), cms);
        var certStore = signedData.GetCertificates();
        var signers = signedData.GetSignerInfos().GetSigners();
        signers.Should().HaveCount(1);

        foreach (SignerInformation si in signers)
        {
            BcX509.X509Certificate cert = certStore.EnumerateMatches(si.SignerID).First();
            si.Verify(cert).Should().BeTrue(because: "the assembled CMS must verify over the signed content");
        }
    }

    // The DER encoding of an OID value embeds the OID bytes; this turns a dotted OID into its DER hex so a
    // substring search over the attribute DER reliably detects the attribute without a full ASN.1 parse.
    private static string OidToHexMarker(string oid)
        => Convert.ToHexString(new Org.BouncyCastle.Asn1.DerObjectIdentifier(oid).GetEncoded());
}
