using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using AqtCoreFW.Identity.Domain.Services;
using Microsoft.Extensions.Logging;
using NetX509 = System.Security.Cryptography.X509Certificates.X509Certificate2;
using NetX509Loader = System.Security.Cryptography.X509Certificates.X509CertificateLoader;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Tier-2 structured document verification — XMLDSig + JWS compact serialization.
///
/// <para><b>XMLDSig</b> (<c>VerifyXmlDsigAsync</c>): uses <c>System.Security.Cryptography.Xml.SignedXml</c>
/// to validate the embedded signature; signer certificate extracted from the
/// <c>KeyInfo/X509Data/X509Certificate</c> node; chain validated via <c>ICertificateChainValidator</c>.
/// </para>
///
/// <para><b>JWS</b> (<c>VerifyJwsAsync</c>): compact serialization parsed manually (3 Base64Url segments);
/// signer certificate extracted from the <c>x5c</c> header claim (first element = leaf DER;
/// remaining = intermediates); chain validated via <c>ICertificateChainValidator</c>.
/// Rejects <c>alg=none</c> unconditionally (CVE-2015-9235 class).</para>
/// </summary>
public sealed class StructuredSignatureService : IStructuredSignatureService
{
    private readonly ICertificateChainValidator _chainValidator;
    private readonly ILogger<StructuredSignatureService> _logger;

    private const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";

    public StructuredSignatureService(
        ICertificateChainValidator chainValidator,
        ILogger<StructuredSignatureService> logger)
    {
        _chainValidator = chainValidator;
        _logger = logger;
    }

    // ─── XMLDSig ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<VerifyResult> VerifyXmlDsigAsync(
        byte[] xmlDsigBytes,
        OcspMode ocspMode,
        CancellationToken ct = default)
    {
        if (xmlDsigBytes is null || xmlDsigBytes.Length == 0)
            return Fail("XMLDSig bytes must not be empty.");

        // 1. Parse XML — XmlResolver=null prevents XXE; PreserveWhitespace required for XMLDSig
        XmlDocument doc;
        try
        {
            doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            var xmlString = Encoding.UTF8.GetString(xmlDsigBytes);
            doc.LoadXml(xmlString);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StructuredSignatureService: XMLDSig parse failed");
            return Fail("XMLDSig document could not be parsed: " + ex.Message);
        }

        // 2. Locate exactly one <Signature> element in the XMLDSig namespace
        var sigElements = doc.GetElementsByTagName("Signature", XmlDsigNamespace);
        if (sigElements.Count == 0)
            return Fail("XMLDSig document contains no <Signature> element.");
        if (sigElements.Count > 1)
            return Fail($"XMLDSig document contains {sigElements.Count} <Signature> elements — expected exactly 1.");

        if (sigElements[0] is not XmlElement sigElement)
            return Fail("XMLDSig <Signature> node is not an XmlElement.");

        // 3. Extract leaf certificate from KeyInfo/X509Data/X509Certificate
        NetX509? leafCert = null;
        try
        {
            var certNodes = doc.GetElementsByTagName("X509Certificate", XmlDsigNamespace);
            if (certNodes.Count == 0)
                return Fail("XMLDSig document contains no <X509Certificate> element in KeyInfo.");

            var certBase64 = certNodes[0]!.InnerText.Trim();
            var certDer = Convert.FromBase64String(certBase64);
            leafCert = NetX509Loader.LoadCertificate(certDer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StructuredSignatureService: could not extract X509Certificate from XMLDSig KeyInfo");
            return Fail("Could not extract signing certificate from XMLDSig KeyInfo: " + ex.Message);
        }

        // 4. Verify cryptographic signature via SignedXml
        //    CheckSignature(cert, verifySignatureOnly: true) — we own chain validation below
        bool sigValid;
        try
        {
            var signedXml = new SignedXml(doc);
            signedXml.LoadXml(sigElement);
            sigValid = signedXml.CheckSignature(leafCert, verifySignatureOnly: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StructuredSignatureService: SignedXml.CheckSignature threw");
            return Fail("XMLDSig signature verification failed: " + ex.Message);
        }

        if (!sigValid)
        {
            return new VerifyResult(
                IsValid: false,
                FailureReason: "XMLDSig cryptographic signature is invalid.",
                SigningCertificate: CertInfo(leafCert),
                ChainResult: null);
        }

        // 5. Certificate chain + OCSP
        var chainResult = await _chainValidator.ValidateAsync(
            leafCert.RawData, intermediates: null, ocspMode, ct).ConfigureAwait(false);

        if (!chainResult.IsValid)
        {
            return new VerifyResult(
                IsValid: false,
                FailureReason: "Certificate chain validation failed: " + chainResult.FailureReason,
                SigningCertificate: CertInfo(leafCert),
                ChainResult: chainResult);
        }

        return new VerifyResult(
            IsValid: true,
            FailureReason: null,
            SigningCertificate: CertInfo(leafCert),
            ChainResult: chainResult);
    }

    // ─── JWS ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<VerifyResult> VerifyJwsAsync(
        string jwsCompact,
        OcspMode ocspMode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jwsCompact))
            return Fail("JWS compact string must not be empty.");

        // 1. Split compact serialization into 3 Base64Url segments
        var parts = jwsCompact.Split('.');
        if (parts.Length != 3)
            return Fail($"JWS compact must have exactly 3 segments separated by '.'; found {parts.Length}.");

        var headerSegment  = parts[0];
        var payloadSegment = parts[1];
        var sigSegment     = parts[2];

        // 2. Decode and parse JWS header (base64url)
        JsonElement header;
        try
        {
            var headerJson = Base64UrlDecode(headerSegment);
            header = JsonDocument.Parse(headerJson).RootElement;
        }
        catch (Exception ex)
        {
            return Fail("JWS header could not be decoded: " + ex.Message);
        }

        // 3. Extract and validate 'alg' — reject none unconditionally (CVE-2015-9235)
        if (!header.TryGetProperty("alg", out var algProp))
            return Fail("JWS header missing 'alg' field.");

        var alg = algProp.GetString() ?? string.Empty;
        if (string.IsNullOrEmpty(alg) || alg.Equals("none", StringComparison.OrdinalIgnoreCase))
            return Fail("JWS 'alg=none' is rejected — algorithm must be an asymmetric algorithm.");

        // 4. Extract x5c (cert chain) — values are standard base64, not base64url (RFC 7515 §4.1.6)
        if (!header.TryGetProperty("x5c", out var x5cProp) || x5cProp.ValueKind != JsonValueKind.Array)
            return Fail("JWS header missing 'x5c' certificate chain.");

        byte[]? leafDer = null;
        var intermediates = new List<byte[]>();
        try
        {
            var isFirst = true;
            foreach (var x5cElement in x5cProp.EnumerateArray())
            {
                // x5c values are standard base64 (NOT base64url) per RFC 7515 §4.1.6
                var der = Convert.FromBase64String(x5cElement.GetString()!);
                if (isFirst)
                {
                    leafDer = der;
                    isFirst = false;
                }
                else
                {
                    intermediates.Add(der);
                }
            }
        }
        catch (Exception ex)
        {
            return Fail("JWS x5c certificate chain could not be decoded: " + ex.Message);
        }

        if (leafDer is null)
            return Fail("JWS x5c array is empty — no leaf certificate.");

        NetX509 leafCert;
        try
        {
            leafCert = NetX509Loader.LoadCertificate(leafDer);
        }
        catch (Exception ex)
        {
            return Fail("JWS leaf certificate (x5c[0]) is invalid: " + ex.Message);
        }

        // 5. Decode signature bytes (base64url)
        byte[] sigBytes;
        try
        {
            sigBytes = Base64UrlDecodeBytes(sigSegment);
        }
        catch (Exception ex)
        {
            return Fail("JWS signature segment could not be decoded: " + ex.Message);
        }

        if (sigBytes.Length == 0)
            return Fail("JWS signature segment is empty.");

        // 6. Signing input = ASCII bytes of "header.payload"
        var signingInput = Encoding.ASCII.GetBytes(headerSegment + "." + payloadSegment);

        // 7. Verify cryptographic signature according to 'alg'
        bool sigValid;
        try
        {
            sigValid = VerifyJwsSignature(alg, leafCert, signingInput, sigBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StructuredSignatureService: JWS signature verification threw for alg={Alg}", alg);
            return Fail("JWS signature verification failed: " + ex.Message);
        }

        if (!sigValid)
        {
            return new VerifyResult(
                IsValid: false,
                FailureReason: $"JWS cryptographic signature is invalid (alg={alg}).",
                SigningCertificate: CertInfo(leafCert),
                ChainResult: null);
        }

        // 8. Certificate chain + OCSP
        var chainResult = await _chainValidator.ValidateAsync(
            leafDer,
            intermediates.Count > 0 ? intermediates : null,
            ocspMode, ct).ConfigureAwait(false);

        if (!chainResult.IsValid)
        {
            return new VerifyResult(
                IsValid: false,
                FailureReason: "Certificate chain validation failed: " + chainResult.FailureReason,
                SigningCertificate: CertInfo(leafCert),
                ChainResult: chainResult);
        }

        return new VerifyResult(
            IsValid: true,
            FailureReason: null,
            SigningCertificate: CertInfo(leafCert),
            ChainResult: chainResult);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static bool VerifyJwsSignature(string alg, NetX509 cert, byte[] signingInput, byte[] signature)
    {
        return alg.ToUpperInvariant() switch
        {
            "RS256" => VerifyRsa(cert, signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "RS384" => VerifyRsa(cert, signingInput, signature, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1),
            "RS512" => VerifyRsa(cert, signingInput, signature, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
            "PS256" => VerifyRsa(cert, signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
            "PS384" => VerifyRsa(cert, signingInput, signature, HashAlgorithmName.SHA384, RSASignaturePadding.Pss),
            "PS512" => VerifyRsa(cert, signingInput, signature, HashAlgorithmName.SHA512, RSASignaturePadding.Pss),
            "ES256" => VerifyEcdsa(cert, signingInput, signature, HashAlgorithmName.SHA256),
            "ES384" => VerifyEcdsa(cert, signingInput, signature, HashAlgorithmName.SHA384),
            "ES512" => VerifyEcdsa(cert, signingInput, signature, HashAlgorithmName.SHA512),
            _ => throw new NotSupportedException($"JWS algorithm '{alg}' is not supported.")
        };
    }

    private static bool VerifyRsa(NetX509 cert, byte[] data, byte[] signature, HashAlgorithmName hash, RSASignaturePadding padding)
    {
        using var rsa = cert.GetRSAPublicKey()
            ?? throw new InvalidOperationException("Certificate does not contain an RSA public key.");
        return rsa.VerifyData(data, signature, hash, padding);
    }

    private static bool VerifyEcdsa(NetX509 cert, byte[] data, byte[] signature, HashAlgorithmName hash)
    {
        using var ecdsa = cert.GetECDsaPublicKey()
            ?? throw new InvalidOperationException("Certificate does not contain an ECDSA public key.");
        // JWS ECDSA signatures use IEEE-P1363 raw r||s concatenation (RFC 7518 §3.4)
        return ecdsa.VerifyData(data, signature, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static string Base64UrlDecode(string base64Url)
    {
        var bytes = Base64UrlDecodeBytes(base64Url);
        return Encoding.UTF8.GetString(bytes);
    }

    private static byte[] Base64UrlDecodeBytes(string base64Url)
    {
        // Convert Base64Url to standard Base64
        var padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "=";  break;
        }
        return Convert.FromBase64String(padded);
    }

    private static CertificateInfo CertInfo(NetX509 cert) =>
        new(
            SerialNumber: cert.SerialNumber,
            Subject: cert.Subject,
            Issuer: cert.Issuer,
            NotBefore: cert.NotBefore.ToUniversalTime(),
            NotAfter: cert.NotAfter.ToUniversalTime(),
            Thumbprint: cert.Thumbprint,
            RawData: cert.RawData);

    private static VerifyResult Fail(string reason) =>
        new(IsValid: false, FailureReason: reason, SigningCertificate: null, ChainResult: null);
}
