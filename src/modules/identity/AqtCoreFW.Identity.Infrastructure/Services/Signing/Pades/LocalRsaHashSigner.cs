using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;

/// <summary>
/// Spike-only <see cref="IRawHashSigner"/> backed by a freshly generated RSA-2048 self-signed certificate. It
/// mirrors the Viettel Mysign hash-signer contract EXACTLY — <see cref="Sign"/> wraps the supplied SHA-256 digest
/// in a <c>DigestInfo(SHA-256)</c> and applies RSASSA-PKCS1-v1_5 — so Phase 3 swaps only the implementation and
/// the PDF/CMS layers stay byte-for-byte unchanged.
///
/// <para>Adobe will show "signature valid, signer not trusted" for the self-signed leaf — that is GREEN for the
/// cryptographic/ByteRange check (the spike proves assembly correctness, not trust).</para>
/// </summary>
public sealed class LocalRsaHashSigner : IRawHashSigner
{
    private readonly AsymmetricKeyParameter _privateKey;

    public IReadOnlyList<BcX509.X509Certificate> SignerChain { get; }

    public LocalRsaHashSigner(string subjectDn = "CN=PAdES Spike Local Signer, O=AqtCoreFW Test, C=VN")
    {
        var rng = new SecureRandom();
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(rng, 2048));
        AsymmetricCipherKeyPair keyPair = keyGen.GenerateKeyPair();
        _privateKey = keyPair.Private;

        var certGen = new X509V3CertificateGenerator();
        certGen.SetSerialNumber(BigInteger.ProbablePrime(120, rng));
        var name = new X509Name(subjectDn);
        certGen.SetSubjectDN(name);
        certGen.SetIssuerDN(name); // self-signed
        certGen.SetNotBefore(DateTime.UtcNow.AddHours(-1));
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
        certGen.SetPublicKey(keyPair.Public);
        certGen.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
        certGen.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature | KeyUsage.NonRepudiation));

        var sigFactory = new Asn1SignatureFactory("SHA256withRSA", keyPair.Private, rng);
        BcX509.X509Certificate cert = certGen.Generate(sigFactory);
        SignerChain = new[] { cert };
    }

    /// <inheritdoc />
    public byte[] Sign(byte[] sha256OfSignedAttributesDer)
    {
        ArgumentNullException.ThrowIfNull(sha256OfSignedAttributesDer);
        if (sha256OfSignedAttributesDer.Length != 32)
            throw new ArgumentException("Expected a 32-byte SHA-256 digest.", nameof(sha256OfSignedAttributesDer));

        // RSASSA-PKCS1-v1_5 over a PRE-COMPUTED digest: wrap the digest in DigestInfo(SHA-256) then apply the
        // type-1 PKCS#1 padding via the RSA private-key operation. This is exactly what a "sign hash" RAS service
        // (Mysign) does internally, so the local and remote signatures are structurally identical.
        var digestInfo = new DigestInfo(
            new AlgorithmIdentifier(NistObjectIdentifiers.IdSha256, DerNull.Instance),
            sha256OfSignedAttributesDer);
        byte[] tbs = digestInfo.GetEncoded(Asn1Encodable.Der);

        var rsa = new Pkcs1Encoding(new RsaBlindedEngine());
        rsa.Init(forEncryption: true, _privateKey); // forEncryption + private key = the signing transform (block type 1)
        return rsa.ProcessBlock(tbs, 0, tbs.Length);
    }
}
