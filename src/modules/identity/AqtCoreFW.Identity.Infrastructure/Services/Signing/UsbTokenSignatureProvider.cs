using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AqtCoreFW.Identity.Domain.Services;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Security;
// Alias BouncyCastle's X509Certificate to avoid ambiguity with .NET's
// System.Security.Cryptography.X509Certificates.X509Certificate now that the namespace is imported
// (required for GetRSAPublicKey/GetECDsaPublicKey extension methods on X509Certificate2).
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;
using NetX509 = System.Security.Cryptography.X509Certificates.X509Certificate2;
using NetX509Loader = System.Security.Cryptography.X509Certificates.X509CertificateLoader;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// VGCA SignService USB Token provider — verify-only.
///
/// <para><b>Architecture (D15 LOCK):</b> The VGCA desktop service runs on the user's machine,
/// listening on <c>wss://127.0.0.1:8987/</c> via <c>vgcaplugin.js</c>. The FE invokes
/// <c>vgca_sign_msg({HashAlg, HashValue, Encode}, callback)</c> through the WSS bridge; the
/// callback returns <c>{ Status:0, Signature, CertBase64 }</c> where <c>Signature</c> is a raw
/// RSA/ECDSA signature over the pre-computed SHA-256 hash of the payload.
/// The FE then POSTs payload + raw signature + signer cert DER to the BE <c>/verify</c> endpoint.
/// </para>
///
/// <para><b>Format routing (W1):</b>
/// <list type="bullet">
///   <item><term><see cref="SignatureFormat.Raw"/></term>
///     <description>New W1 path — raw RSA/ECDSA verify against payload + explicit signer cert DER.
///     Uses <see cref="X509CertificateLoader.LoadCertificate(byte[])"/> (net10 SYSLIB0057-safe). </description></item>
///   <item><term><see cref="SignatureFormat.Pkcs7"/></term>
///     <description>Legacy CMS path — preserved for backward compatibility with all existing callers
///     (169 integration tests default to Pkcs7).</description></item>
/// </list>
/// </para>
///
/// <para><b>BE never holds the private key.</b> <see cref="SignAsync"/> throws
/// <see cref="NotSupportedException"/> — <see cref="SignatureProviderCapabilities.ClientSideSigning"/>
/// declares that signing happens FE-side.</para>
/// </summary>
public sealed class UsbTokenSignatureProvider : IDigitalSignatureProvider
{
    private readonly ICertificateChainValidator _chainValidator;
    private readonly ILogger<UsbTokenSignatureProvider> _logger;

    /// <summary>
    /// Initialises the VGCA USB Token provider.
    /// </summary>
    /// <param name="chainValidator">Certificate chain validator (W3 <see cref="CertificateChainValidator"/>
    /// wired by W6 DI registration).</param>
    /// <param name="logger">Logger.</param>
    public UsbTokenSignatureProvider(
        ICertificateChainValidator chainValidator,
        ILogger<UsbTokenSignatureProvider> logger)
    {
        _chainValidator = chainValidator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string ProviderKey => "usb-vgca";

    /// <inheritdoc/>
    public string DisplayName => "VGCA SignService (USB Token)";

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="SignatureProviderCapabilities.ClientSideSigning"/> — signing happens FE-side
    /// via <c>vgcaplugin.js</c> + WSS bridge to the VGCA desktop service.
    /// <see cref="SignatureProviderCapabilities.RequiresLocalAgent"/> — the FE capability probe
    /// calls <c>vgca_get_version(cb)</c> to check if the desktop service is installed and running.
    /// <see cref="SignatureProviderCapabilities.RawSign"/> — W1: <c>vgca_sign_msg</c> returns raw
    /// RSA/ECDSA bytes (not CMS); FE sends <c>Format=Raw + SignerCertBase64</c> for verify.
    /// <see cref="SignatureProviderCapabilities.XmlDsig"/> — W2: <c>vgca_sign_xml</c> returns a
    /// self-contained XMLDSig document; verified via <c>IStructuredSignatureService.VerifyXmlDsigAsync</c>.
    /// <see cref="SignatureProviderCapabilities.JsonSign"/> — W2: <c>vgca_sign_json</c> returns a
    /// JWS compact serialization; verified via <c>IStructuredSignatureService.VerifyJwsAsync</c>.
    /// </remarks>
    public SignatureProviderCapabilities Capabilities =>
        SignatureProviderCapabilities.ClientSideSigning |
        SignatureProviderCapabilities.RequiresLocalAgent |
        SignatureProviderCapabilities.RawSign |
        SignatureProviderCapabilities.XmlDsig |
        SignatureProviderCapabilities.JsonSign |
        SignatureProviderCapabilities.DocFlow; // W3: Tier-3 doc-flow PDF signing

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// Always thrown. VGCA USB Token signing is FE-side via <c>vgcaplugin.js</c>
    /// → <c>wss://127.0.0.1:8987/SignMsg</c>. The BE never holds the private key.
    /// Check <see cref="SignatureProviderCapabilities.ClientSideSigning"/> before calling.
    /// </exception>
    public Task<SignResult> SignAsync(SignRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "VGCA USB Token signs FE-side via vgcaplugin.js → wss://127.0.0.1:8987/. " +
            "BE never holds the private key. Capability=ClientSideSigning.");

    /// <inheritdoc/>
    /// <remarks>
    /// Routes on <see cref="VerifyRequest.Format"/>:
    /// <list type="bullet">
    ///   <item><term><see cref="SignatureFormat.Raw"/> (W1 new path)</term>
    ///     <description>Computes hash of <see cref="VerifyRequest.Payload"/>, verifies raw
    ///     RSA/ECDSA bytes against it using the public key from <see cref="VerifyRequest.SignerCertDer"/>.
    ///     Certificate DER is required; embed cert not available in raw format.</description></item>
    ///   <item><term><see cref="SignatureFormat.Pkcs7"/> (legacy — default)</term>
    ///     <description>Original CMS/PKCS#7 path preserved for backward compatibility.</description></item>
    /// </list>
    /// </remarks>
    public async Task<VerifyResult> VerifyAsync(
        VerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Route to raw RSA/ECDSA path for W1 Tier-1 format
        if (request.Format == SignatureFormat.Raw)
            return await VerifyRawAsync(request, cancellationToken).ConfigureAwait(false);

        // --- Guard: empty inputs (Pkcs7 path) ---
        if (request.Payload is null || request.Payload.Length == 0)
            return Fail("Payload is empty");

        if (request.Signature is null || request.Signature.Length == 0)
            return Fail("Signature bytes are empty");

        // --- 1. Parse CMS/PKCS#7 detached signature ---
        CmsSignedData cms;
        try
        {
            // CmsSignedData(content, encoded) constructor accepts:
            //   content = the original payload (detached — not embedded in signature)
            //   encoded = the raw CMS SignedData DER bytes
            var processable = new CmsProcessableByteArray(request.Payload);
            cms = new CmsSignedData(processable, request.Signature);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse CMS/PKCS#7 signature bytes (VGCA USB Token)");
            return Fail("CMS signature could not be parsed: " + ex.Message);
        }

        // --- 2. Verify CMS signature against payload + extract signer cert ---
        var signerInfos = cms.GetSignerInfos();
        var signers = signerInfos.GetSigners();

        if (!signers.GetEnumerator().MoveNext())
        {
            return Fail("CMS structure contains no signers");
        }

        // Pick the first signer (VGCA USB Token produces a single signer)
        SignerInformation? matchedSigner = null;
        BcX509Certificate? signerCert = null;

        // Get certificates embedded in the CMS structure
        var certStore = cms.GetCertificates();

        foreach (SignerInformation signer in signers)
        {
            // Find matching cert for this signer ID
            var matchedCerts = certStore.EnumerateMatches(signer.SignerID);
            BcX509Certificate? candidate = null;
            foreach (BcX509Certificate c in matchedCerts)
            {
                candidate = c;
                break;
            }

            if (candidate is null)
            {
                _logger.LogWarning("VGCA CMS: No certificate found for signer ID {SignerId}", signer.SignerID);
                continue;
            }

            // Verify the CMS signature against the payload (signer.Verify internally computes digest over content)
            bool signatureValid;
            try
            {
                signatureValid = signer.Verify(candidate);
            }
            catch (CmsException ex)
            {
                _logger.LogWarning(ex, "VGCA CMS signature verification threw CmsException");
                return Fail("CMS signature verification failed: " + ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VGCA CMS signature verification threw unexpected exception");
                return Fail("CMS signature verification error: " + ex.Message);
            }

            if (!signatureValid)
            {
                return Fail("CMS signature is invalid — payload may have been tampered");
            }

            matchedSigner = signer;
            signerCert = candidate;
            break;
        }

        if (signerCert is null || matchedSigner is null)
        {
            return Fail("No valid signer with matching certificate found in CMS structure");
        }

        // --- 3. Build CertificateInfo from the signing cert ---
        byte[] certDer = signerCert.GetEncoded();
        var certInfo = new CertificateInfo(
            SerialNumber: signerCert.SerialNumber.ToString(16).ToUpperInvariant(),
            Subject: signerCert.SubjectDN.ToString(),
            Issuer: signerCert.IssuerDN.ToString(),
            NotBefore: signerCert.NotBefore,
            NotAfter: signerCert.NotAfter,
            Thumbprint: VnCaRootStore.ComputeThumbprint(certDer),
            RawData: certDer);

        // --- 4. Extract intermediate certificates from CMS bag (if any) ---
        var intermediates = new List<byte[]>();
        try
        {
            foreach (BcX509Certificate c in certStore.EnumerateMatches(null))
            {
                // Skip the end-entity signer cert (already processed)
                if (c.SerialNumber.Equals(signerCert.SerialNumber) &&
                    c.IssuerDN.Equivalent(signerCert.IssuerDN))
                    continue;
                intermediates.Add(c.GetEncoded());
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — intermediates are optional; chain validator handles empty list
            _logger.LogDebug(ex, "Could not extract intermediate certs from CMS bag — proceeding without them");
        }

        // --- 5. Delegate chain validation to ICertificateChainValidator (W3) ---
        ChainValidationResult chainResult;
        try
        {
            chainResult = await _chainValidator.ValidateAsync(
                certDer,
                intermediates.Count > 0 ? intermediates : null,
                request.OcspMode,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chain validator threw unexpectedly for VGCA cert subject={Subject}",
                signerCert.SubjectDN);
            return new VerifyResult(
                IsValid: false,
                FailureReason: "Chain validation failed unexpectedly: " + ex.Message,
                SigningCertificate: certInfo,
                ChainResult: null);
        }

        // --- 6. Compose result — both CMS verify AND chain must pass ---
        bool isValid = chainResult.IsValid;
        string? failureReason = chainResult.IsValid ? null : chainResult.FailureReason;

        _logger.LogInformation(
            "VGCA USB Token verify complete: IsValid={IsValid}, Subject={Subject}, OcspChecked={OcspChecked}",
            isValid, signerCert.SubjectDN, chainResult.OcspChecked);

        return new VerifyResult(
            IsValid: isValid,
            FailureReason: failureReason,
            SigningCertificate: certInfo,
            ChainResult: chainResult);
    }

    // ── Raw verify (W1 Tier-1) ───────────────────────────────────────────────

    /// <summary>
    /// Verifies a raw RSA/ECDSA signature (W1 Tier-1 path).
    /// Called when <see cref="VerifyRequest.Format"/> == <see cref="SignatureFormat.Raw"/>.
    ///
    /// <para>Flow:
    /// <list type="number">
    ///   <item>Guard: <see cref="VerifyRequest.SignerCertDer"/> must be present.</item>
    ///   <item>Load cert via <see cref="X509CertificateLoader.LoadCertificate(byte[])"/> (SYSLIB0057-safe for net10).</item>
    ///   <item>Compute hash of <see cref="VerifyRequest.Payload"/> using <see cref="VerifyRequest.HashAlg"/>.</item>
    ///   <item>Verify sig based on <see cref="VerifyRequest.Encode"/>: RSA-PKCS#1-v1.5 or ECDSA-IEEE-P1363.</item>
    ///   <item>Build <see cref="CertificateInfo"/> + delegate chain to <see cref="ICertificateChainValidator"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    private async Task<VerifyResult> VerifyRawAsync(VerifyRequest request, CancellationToken ct)
    {
        // --- Guard: empty inputs ---
        if (request.Payload is null || request.Payload.Length == 0)
            return Fail("Payload is empty");

        if (request.Signature is null || request.Signature.Length == 0)
            return Fail("Signature bytes are empty");

        if (request.SignerCertDer is null || request.SignerCertDer.Length == 0)
            return Fail("SignerCertDer required for Raw format — VGCA returns CertBase64 separately from signature");

        // --- 1. Load certificate (X509CertificateLoader avoids SYSLIB0057 on net10) ---
        NetX509 cert;
        try
        {
            cert = NetX509Loader.LoadCertificate(request.SignerCertDer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load signer certificate DER bytes (Raw format)");
            return Fail("Could not parse signer certificate: " + ex.Message);
        }

        using (cert) // X509Certificate2 implements IDisposable
        {
            // --- 2. Compute hash ---
            var hashAlg = (request.HashAlg ?? "SHA256").ToUpperInvariant();
            byte[] hash;
            try
            {
                hash = hashAlg switch
                {
                    "SHA256" => SHA256.HashData(request.Payload),
                    "SHA384" => SHA384.HashData(request.Payload),
                    "SHA512" => SHA512.HashData(request.Payload),
                    _ => throw new NotSupportedException($"Unsupported HashAlg: {request.HashAlg}")
                };
            }
            catch (NotSupportedException ex)
            {
                return Fail(ex.Message);
            }

            var hashAlgName = hashAlg switch
            {
                "SHA256" => HashAlgorithmName.SHA256,
                "SHA384" => HashAlgorithmName.SHA384,
                "SHA512" => HashAlgorithmName.SHA512,
                _ => HashAlgorithmName.SHA256
            };

            // --- 3. Verify signature ---
            bool sigValid;
            var encode = (request.Encode ?? "RSA").ToUpperInvariant();
            try
            {
                sigValid = encode switch
                {
                    "RSA" => VerifyRsa(cert, hash, request.Signature, hashAlgName),
                    "ECDSA" or "EC" => VerifyEcdsa(cert, hash, request.Signature),
                    _ => throw new NotSupportedException($"Unsupported Encode: {request.Encode}")
                };
            }
            catch (NotSupportedException ex)
            {
                return Fail(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Raw signature verification threw unexpected exception");
                return Fail("Raw signature verification error: " + ex.Message);
            }

            if (!sigValid)
                return Fail("Raw signature verification failed — payload may have been tampered");

            // --- 4. Build CertificateInfo ---
            byte[] certDer = cert.RawData;
            var certInfo = new CertificateInfo(
                SerialNumber: cert.SerialNumber,
                Subject: cert.SubjectName.Name,
                Issuer: cert.IssuerName.Name,
                NotBefore: cert.NotBefore,
                NotAfter: cert.NotAfter,
                Thumbprint: VnCaRootStore.ComputeThumbprint(certDer),
                RawData: certDer);

            // --- 5. Chain validation ---
            ChainValidationResult chainResult;
            try
            {
                chainResult = await _chainValidator.ValidateAsync(
                    certDer,
                    intermediates: null,
                    request.OcspMode,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chain validator threw unexpectedly for Raw cert subject={Subject}", cert.SubjectName.Name);
                return new VerifyResult(
                    IsValid: false,
                    FailureReason: "Chain validation failed unexpectedly: " + ex.Message,
                    SigningCertificate: certInfo,
                    ChainResult: null);
            }

            bool isValid = chainResult.IsValid;
            string? failureReason = chainResult.IsValid ? null : chainResult.FailureReason;

            _logger.LogInformation(
                "VGCA USB Token Raw verify complete: IsValid={IsValid}, Subject={Subject}, OcspChecked={OcspChecked}",
                isValid, cert.SubjectName.Name, chainResult.OcspChecked);

            return new VerifyResult(
                IsValid: isValid,
                FailureReason: failureReason,
                SigningCertificate: certInfo,
                ChainResult: chainResult);
        }
    }

    private static bool VerifyRsa(
        NetX509 cert,
        byte[] hash,
        byte[] signature,
        HashAlgorithmName hashAlgName)
    {
        using var rsa = cert.GetRSAPublicKey()
            ?? throw new InvalidOperationException("Certificate does not contain an RSA public key");
        return rsa.VerifyHash(hash, signature, hashAlgName, RSASignaturePadding.Pkcs1);
    }

    private static bool VerifyEcdsa(NetX509 cert, byte[] hash, byte[] signature)
    {
        using var ecdsa = cert.GetECDsaPublicKey()
            ?? throw new InvalidOperationException("Certificate does not contain an ECDSA public key");
        // VGCA returns IEEE-P1363 fixed-field concatenation format (r||s), not DER
        return ecdsa.VerifyHash(hash, signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static VerifyResult Fail(string reason)
        => new(IsValid: false, FailureReason: reason, SigningCertificate: null, ChainResult: null);
}
