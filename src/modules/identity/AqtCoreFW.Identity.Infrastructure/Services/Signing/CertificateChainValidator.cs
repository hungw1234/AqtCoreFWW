using AqtCoreFW.Identity.Domain.Services;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Pkix;
using Org.BouncyCastle.Utilities.Collections;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Validates certificate chains against VN CA trust anchors using BouncyCastle PKIX path building.
///
/// <para><b>Validation steps:</b></para>
/// <list type="number">
///   <item>Parse DER bytes → BouncyCastle X509Certificate.</item>
///   <item>Build chain: end-entity → intermediates → root via PKIX path builder.</item>
///   <item>Verify per-link signatures.</item>
///   <item>Pin root thumbprint against <see cref="VnCaRootStore"/>.</item>
///   <item>OCSP check on end-entity cert via AIA responder URL (D-V3 Hybrid).</item>
/// </list>
///
/// <para><b>OCSP mode semantics (D-V3 Hybrid):</b></para>
/// <list type="bullet">
///   <item>Soft + unreachable → <c>OcspChecked=true, OcspSucceeded=false, IsValid=true</c> (warn-log).</item>
///   <item>Hard + unreachable → <c>OcspChecked=true, OcspSucceeded=false, IsValid=false</c>.</item>
///   <item>Revoked → <c>IsValid=false</c> in ALL modes.</item>
///   <item>No <see cref="IOcspClient"/> injected → OCSP skipped, <c>OcspChecked=false</c>.</item>
/// </list>
/// </summary>
public sealed class CertificateChainValidator(
    VnCaRootStore rootStore,
    ILogger<CertificateChainValidator> logger,
    IOcspClient? ocspClient = null,
    VnCaIntermediateStore? intermediateStore = null) : ICertificateChainValidator
{
    private readonly BcX509.X509CertificateParser _parser = new();

    /// <inheritdoc/>
    public async Task<ChainValidationResult> ValidateAsync(
        byte[] certRaw,
        IEnumerable<byte[]>? intermediates,
        OcspMode mode,
        CancellationToken ct)
    {
        // --- 1. Parse end-entity cert ---
        BcX509.X509Certificate eeCert;
        try
        {
            eeCert = _parser.ReadCertificate(certRaw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse end-entity certificate DER bytes");
            return Fail("Certificate could not be parsed: " + ex.Message);
        }

        // --- 2. Parse intermediates ---
        var intermediateList = new List<BcX509.X509Certificate>();
        if (intermediates is not null)
        {
            foreach (var der in intermediates)
            {
                try { intermediateList.Add(_parser.ReadCertificate(der)); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse intermediate certificate DER bytes — skipping");
                }
            }
        }

        // --- 3. Check validity window on end-entity cert ---
        var now = DateTime.UtcNow;
        try
        {
            eeCert.CheckValidity(now);
        }
        catch (Exception ex)
        {
            return Fail("Certificate validity check failed: " + ex.Message);
        }

        // --- 4. Build PKIX path + verify per-link signatures ---
        var trustedRoots = rootStore.GetTrustedRoots();
        var trustedThumbprints = rootStore.GetTrustedRootThumbprints();

        // Collect chain certs: EE + caller-supplied intermediates + store candidates + roots.
        // Intermediates from VnCaIntermediateStore are path-building candidates only —
        // they must never be added to trustAnchors or the pin-set. Keeping them separate
        // ensures a misconfigured intermediate cannot anchor the chain.
        var storeCandidates = intermediateStore?.GetCandidates() ?? [];
        var allCerts = new List<BcX509.X509Certificate>(
            intermediateList.Count + storeCandidates.Count + trustedRoots.Count + 1)
        {
            eeCert
        };
        allCerts.AddRange(intermediateList);
        allCerts.AddRange(storeCandidates); // store candidates supplement caller-supplied intermediates
        allCerts.AddRange(trustedRoots);

        // Build trust anchors from root store
        var trustAnchors = new HashSet<TrustAnchor>();
        foreach (var root in trustedRoots)
        {
            trustAnchors.Add(new TrustAnchor(root, null));
        }

        // If no trust anchors are configured (PRODUCTION_SEED_DEFERRED) → fail with clear message
        if (trustAnchors.Count == 0)
        {
            return Fail("No trusted root CA certificates configured. PRODUCTION_SEED_DEFERRED — load VNPT-CA and VGCA roots via NEAC acquisition.");
        }

        // PKIX chain validation
        BcX509.X509Certificate? rootCert = null;
        var chainCerts = new List<CertificateInfo>();
        // Certs from the PKIX-built path — needed outside the try block for OCSP issuer resolution
        IList<BcX509.X509Certificate> builtPathCerts = [];

        try
        {
            // BC 2.x uses ISelector<X509Certificate> — use an exact-cert equality selector
            var targetSelector = new EeCertSelector(eeCert);
            var certStore = CollectionUtilities.CreateStore(allCerts);
            var builderParams = new PkixBuilderParameters(trustAnchors, targetSelector)
            {
                IsRevocationEnabled = false // OCSP handled manually below; CRL disabled per D-V3
            };
            builderParams.AddStoreCert(certStore);

            var builder = new PkixCertPathBuilder();
            var result = builder.Build(builderParams);

            builtPathCerts = result.CertPath.Certificates;

            // Extract chain in order (EE first, root last)
            foreach (var cert in builtPathCerts)
            {
                chainCerts.Add(ToCertificateInfo(cert));
            }

            // Find the trust anchor cert used to close the chain
            rootCert = result.TrustAnchor.TrustedCert;
            if (rootCert is not null)
            {
                chainCerts.Add(ToCertificateInfo(rootCert));
            }
        }
        catch (PkixCertPathBuilderException ex)
        {
            logger.LogWarning(ex, "PKIX chain build failed for certificate subject={Subject}", eeCert.SubjectDN);
            return Fail("Certificate chain validation failed: " + ex.Message, chainCerts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error during PKIX chain build for subject={Subject}", eeCert.SubjectDN);
            return Fail("Unexpected chain validation error: " + ex.Message, chainCerts);
        }

        // --- 5. Root thumbprint pin check ---
        // Double-check: BouncyCastle PKIX accepted the chain but we re-confirm the root is in our pin set.
        if (rootCert is not null)
        {
            var rootThumbprint = VnCaRootStore.ComputeThumbprint(rootCert.GetEncoded());
            if (!trustedThumbprints.Contains(rootThumbprint))
            {
                return Fail(
                    $"Root certificate thumbprint '{rootThumbprint}' is not in the VN CA trust store pin set.",
                    chainCerts);
            }
        }

        // --- 6. OCSP check (D-V3 Hybrid) ---
        // Build the issuer candidate list for OCSP by merging caller-supplied intermediates with
        // certs from the PKIX-built chain. This ensures OCSP runs on raw-path (intermediates:null)
        // when the issuer cert is discoverable from the built chain or the intermediate store.
        var ocspIssuers = BuildOcspIssuerCandidates(intermediateList, builtPathCerts);
        var (ocspChecked, ocspSucceeded, ocspFailReason) = await CheckOcspAsync(
            certRaw, ocspIssuers, eeCert, mode, ct);

        if (ocspFailReason is not null)
        {
            return new ChainValidationResult(
                IsValid: false,
                FailureReason: ocspFailReason,
                Chain: chainCerts,
                OcspChecked: ocspChecked,
                OcspSucceeded: ocspSucceeded);
        }

        return new ChainValidationResult(
            IsValid: true,
            FailureReason: null,
            Chain: chainCerts,
            OcspChecked: ocspChecked,
            OcspSucceeded: ocspSucceeded);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the issuer candidate list for OCSP by merging caller-supplied intermediates with
    /// certificates from the PKIX-built chain path. On raw-path calls (intermediates arg = null),
    /// the built chain is the only source for the issuer cert — without this merge OCSP would
    /// silently skip because FindIssuerDer would find nothing.
    /// </summary>
    private static List<BcX509.X509Certificate> BuildOcspIssuerCandidates(
        IList<BcX509.X509Certificate> callerIntermediates,
        IList<BcX509.X509Certificate> builtChainCerts)
    {
        // Start with caller-supplied intermediates (preserves existing behaviour when provided)
        var candidates = new List<BcX509.X509Certificate>(callerIntermediates);

        // Supplement with certs from the PKIX-built path that aren't already present
        foreach (var cert in builtChainCerts)
        {
            if (!candidates.Any(c => c.Equals(cert)))
                candidates.Add(cert);
        }

        return candidates;
    }

    /// <summary>
    /// Performs OCSP check against the responder URL from the cert's AIA extension.
    /// Returns (checked, succeeded, failureReason?) — failureReason non-null means IsValid=false.
    /// </summary>
    private async Task<(bool Checked, bool Succeeded, string? FailureReason)> CheckOcspAsync(
        byte[] certRaw,
        IList<BcX509.X509Certificate> intermediates,
        BcX509.X509Certificate eeCert,
        OcspMode mode,
        CancellationToken ct)
    {
        if (ocspClient is null)
            return (false, false, null); // OCSP not configured — skip (OcspChecked=false)

        // Find issuer cert: first intermediate whose subject = eeCert issuer, else fallback to any intermediate
        byte[]? issuerDer = FindIssuerDer(eeCert, intermediates);
        if (issuerDer is null)
        {
            logger.LogWarning("OCSP skipped — no issuer certificate found in intermediates for subject={Subject}", eeCert.SubjectDN);
            return (false, false, null);
        }

        OcspCertificateStatus status;
        try
        {
            status = await ocspClient.CheckAsync(certRaw, issuerDer, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OCSP client threw unexpectedly for subject={Subject}", eeCert.SubjectDN);
            status = OcspCertificateStatus.Unreachable;
        }

        return status switch
        {
            OcspCertificateStatus.Good => (true, true, null),

            OcspCertificateStatus.Revoked =>
                (true, false, "Certificate has been revoked (OCSP status: Revoked)."),

            OcspCertificateStatus.Unreachable when mode == OcspMode.Soft =>
                LogAndReturn(true, false, null,
                    "OCSP responder unreachable for subject={Subject} — mode=Soft, continuing as valid", eeCert),

            OcspCertificateStatus.Unreachable when mode == OcspMode.Hard =>
                (true, false, "OCSP responder unreachable and mode=Hard — certificate rejected."),

            OcspCertificateStatus.Unknown =>
                LogAndReturn(true, false, null,
                    "OCSP returned Unknown for subject={Subject} — treating as non-fatal", eeCert),

            _ => (true, false, $"Unexpected OCSP status: {status}")
        };
    }

    private (bool, bool, string?) LogAndReturn(
        bool checked_, bool succeeded, string? reason,
        string messageTemplate, BcX509.X509Certificate cert)
    {
        logger.LogWarning(messageTemplate, cert.SubjectDN);
        return (checked_, succeeded, reason);
    }

    /// <summary>Finds the DER bytes of the issuer certificate from the intermediate list.</summary>
    private static byte[]? FindIssuerDer(
        BcX509.X509Certificate subject,
        IList<BcX509.X509Certificate> candidates)
    {
        foreach (var candidate in candidates)
        {
            // Match by subject DN equality
            if (candidate.SubjectDN.Equivalent(subject.IssuerDN))
                return candidate.GetEncoded();
        }
        return null;
    }

    private static ChainValidationResult Fail(string reason, IReadOnlyList<CertificateInfo>? chain = null)
        => new(false, reason, chain ?? [], false, false);

    private static CertificateInfo ToCertificateInfo(BcX509.X509Certificate cert)
    {
        var der = cert.GetEncoded();
        return new CertificateInfo(
            SerialNumber: cert.SerialNumber.ToString(16).ToUpperInvariant(),
            Subject: cert.SubjectDN.ToString(),
            Issuer: cert.IssuerDN.ToString(),
            NotBefore: cert.NotBefore,
            NotAfter: cert.NotAfter,
            Thumbprint: VnCaRootStore.ComputeThumbprint(der),
            RawData: der);
    }

    // -------------------------------------------------------------------------
    // BC 2.x ISelector<X509Certificate> — matches exactly the target end-entity cert.
    // BC 2.x removed X509CertStoreSelector; callers supply ISelector<T> directly.
    // -------------------------------------------------------------------------
    private sealed class EeCertSelector(BcX509.X509Certificate target)
        : ISelector<BcX509.X509Certificate>
    {
        public bool Match(BcX509.X509Certificate? cert)
            => cert is not null && cert.Equals(target);

        public object Clone() => new EeCertSelector(target);
    }
}
