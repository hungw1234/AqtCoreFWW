using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Integration.Tests.Signing.Fixtures;

/// <summary>
/// Test fixture that programmatically generates a self-signed test certificate
/// and a signed CMS/PKCS#7 sample matching the VGCA <c>vgcaplugin.js</c> callback shape.
///
/// <para>
/// No real VGCA device or network call is needed. BouncyCastle generates a 2048-bit RSA
/// key pair + a self-signed X.509v3 certificate at fixture construction time, then uses
/// <see cref="CmsSignedDataGenerator"/> to produce a detached CMS signature over a known
/// test payload. The same cert is returned for chain-validation mocking.
/// </para>
///
/// <para>
/// <b>Reference shape (VGCA callback):</b><br/>
/// JS callback returns <c>{ Status, Signature, ... }</c> where <c>Signature</c> is
/// base64-encoded CMS/PKCS#7 DER bytes (detached, signer cert embedded in CMS bag).
/// The BE receives the decoded bytes in <see cref="VerifyRequest.Signature"/>.
/// </para>
/// </summary>
public sealed class VgcaGoldenSamples
{
    /// <summary>The test payload used to generate the golden signature sample.</summary>
    public static readonly byte[] GoldenPayload = "VGCA-W4-golden-test-payload-2026"u8.ToArray();

    /// <summary>DER-encoded detached CMS/PKCS#7 signature over <see cref="GoldenPayload"/>.</summary>
    public byte[] GoldenSignature { get; }

    /// <summary>DER-encoded self-signed test certificate (the signer cert).</summary>
    public byte[] SignerCertDer { get; }

    /// <summary>BouncyCastle signer certificate for test root store seeding.</summary>
    public BcX509.X509Certificate SignerCertBc { get; }

    private static readonly SecureRandom Rng = new();

    /// <summary>
    /// Generates RSA-2048 key pair, self-signed test cert, and golden CMS signature at construction.
    /// Deterministic across fixture instances within the same test run (cert generated fresh each time
    /// but the shape is stable — used for structural verification, not key pinning).
    /// </summary>
    public VgcaGoldenSamples()
    {
        // 1. Generate RSA-2048 key pair
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(Rng, 2048));
        AsymmetricCipherKeyPair keyPair = keyGen.GenerateKeyPair();

        // 2. Build a self-signed X.509v3 certificate
        var certGen = new X509V3CertificateGenerator();
        certGen.SetSerialNumber(BigInteger.ProbablePrime(120, Rng));
        var subject = new X509Name("CN=VGCA Test Signer W4, O=AqtCoreFW Test CA, C=VN");
        certGen.SetSubjectDN(subject);
        certGen.SetIssuerDN(subject);                        // self-signed
        certGen.SetNotBefore(DateTime.UtcNow.AddHours(-1)); // account for clock skew
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
        certGen.SetPublicKey(keyPair.Public);

        // Basic Constraints: leaf certificate (CA:false)
        certGen.AddExtension(
            X509Extensions.BasicConstraints,
            critical: true,
            extensionValue: new BasicConstraints(false));

        // Key Usage: digitalSignature
        certGen.AddExtension(
            X509Extensions.KeyUsage,
            critical: true,
            extensionValue: new KeyUsage(KeyUsage.DigitalSignature));

        var signatureFactory = new Asn1SignatureFactory("SHA256withRSA", keyPair.Private, Rng);
        SignerCertBc = certGen.Generate(signatureFactory);
        SignerCertDer = SignerCertBc.GetEncoded();

        // 3. Produce CMS/PKCS#7 detached signature over GoldenPayload
        GoldenSignature = SignCms(GoldenPayload, keyPair, SignerCertBc);
    }

    /// <summary>
    /// Signs <paramref name="payload"/> using <paramref name="keyPair"/> and embeds
    /// <paramref name="cert"/> in the CMS bag. Returns detached CMS DER bytes.
    /// </summary>
    /// <remarks>
    /// Reusable from tests to create signatures over arbitrary payloads with the same key.
    /// </remarks>
    public static byte[] SignCms(
        byte[] payload,
        AsymmetricCipherKeyPair keyPair,
        BcX509.X509Certificate cert)
    {
        var gen = new CmsSignedDataGenerator();

        // Add signer: SHA256withRSA, signer cert embedded in SignerInfo
        gen.AddSignerInfoGenerator(
            new SignerInfoGeneratorBuilder()
                .Build(new Asn1SignatureFactory("SHA256withRSA", keyPair.Private, Rng), cert));

        // Embed cert in CMS bag so the verifier can extract it
        gen.AddCertificates(CollectionUtilities.CreateStore(new[] { cert }));

        // Generate detached signature (encapsulate=false → content not embedded)
        var processable = new CmsProcessableByteArray(payload);
        CmsSignedData signed = gen.Generate(processable, encapsulate: false);
        return signed.GetEncoded();
    }

    /// <summary>
    /// Generates a fresh RSA-2048 key pair. Convenience factory for tamper tests
    /// that need a separate key to produce an invalid signature.
    /// </summary>
    public static AsymmetricCipherKeyPair GenerateKeyPair()
    {
        var kg = new RsaKeyPairGenerator();
        kg.Init(new KeyGenerationParameters(Rng, 2048));
        return kg.GenerateKeyPair();
    }
}
