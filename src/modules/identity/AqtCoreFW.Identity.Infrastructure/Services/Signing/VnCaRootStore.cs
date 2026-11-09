using System.Security.Cryptography;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Immutable trust anchor store seeded with VN CA root certificates.
///
/// <para>
/// <b>D17 Scope:</b> Seeds VNPT-CA + VGCA roots ONLY. Other providers (MiSA, Viettel-CA)
/// are future additions — one line per provider, no rework to core chain validation logic.
/// </para>
///
/// <para>
/// <b>PRODUCTION_SEED_DEFERRED</b> — VN CA root certificate PEM/DER bytes are pending
/// acquisition via NEAC (Ban Cơ Yếu Chính Phủ). Replace the placeholder stubs below with
/// real DER bytes once the NEAC root response is received. Track in compliance issue tracker.
/// </para>
///
/// <para>
/// For testing, inject a custom set of root certificates via
/// <see cref="VnCaRootStore(IEnumerable{byte[]})"/>. This constructor is internal and
/// intended only for unit/integration test usage.
/// </para>
/// </summary>
public sealed class VnCaRootStore
{
    // SHA-1 thumbprint format: uppercase hex, no separators (e.g. "AB12CD34...").
    // These are PLACEHOLDER thumbprints — replace with real NEAC-issued root thumbprints
    // once root certificates are acquired. Mark deferred in compliance dossier.
    // PRODUCTION_SEED_DEFERRED
    private static readonly string[] _productionThumbprints =
    [
        // VNPT-CA root (Government PKI level 2) — PRODUCTION_SEED_DEFERRED
        // Source: NEAC root bundle, to be acquired from vnpca.vnpt.vn
        // "VNPT_CA_ROOT_THUMBPRINT_PLACEHOLDER",

        // VGCA root (Ban Cơ Yếu Chính Phủ — Government Cipher Authority) — PRODUCTION_SEED_DEFERRED
        // Source: NEAC root bundle, to be acquired from ca.gov.vn
        // "VGCA_ROOT_THUMBPRINT_PLACEHOLDER",
    ];

    private readonly List<BcX509.X509Certificate> _trustedRoots;
    private readonly HashSet<string> _trustedThumbprints;

    /// <summary>
    /// Initialises the production store. Contains no trusted roots until NEAC root
    /// certificates are acquired and injected (PRODUCTION_SEED_DEFERRED).
    /// Config-based hot-fill is the intended production path.
    /// </summary>
    public VnCaRootStore()
    {
        _trustedRoots = [];
        _trustedThumbprints = new HashSet<string>(_productionThumbprints, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads trusted roots from raw cert bytes (DER or PEM — BouncyCastle's parser auto-detects).
    /// Used by both the config-driven startup factory (production trust seed) and unit tests.
    /// Each root's pin thumbprint is computed from the parsed cert's canonical DER, so it stays
    /// consistent with the chain validator's pin check regardless of input encoding.
    /// </summary>
    /// <param name="rootCertBytes">CA certificates to trust, in DER or PEM encoding.</param>
    internal VnCaRootStore(IEnumerable<byte[]> rootCertBytes)
    {
        _trustedRoots = [];
        _trustedThumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var parser = new BcX509.X509CertificateParser();
        foreach (var der in rootCertBytes)
        {
            var cert = parser.ReadCertificate(der);
            _trustedRoots.Add(cert);
            // Hash the parsed cert's canonical DER, not the raw input bytes — a PEM-armored
            // file would otherwise pin the SHA-1 of the base64 text, never matching the
            // chain validator's pin check (which hashes the built root's GetEncoded()).
            _trustedThumbprints.Add(ComputeThumbprint(cert.GetEncoded()));
        }
    }

    /// <summary>
    /// Number of trusted root CA certificates in the store.
    /// Zero until NEAC root certificates are seeded (PRODUCTION_SEED_DEFERRED).
    /// Used by <see cref="SigningHealthCheckService"/> for startup warnings.
    /// </summary>
    public int TrustedRootCount => _trustedRoots.Count;

    /// <summary>
    /// Returns all trusted root CA certificates in BouncyCastle form.
    /// Empty until NEAC root certificates are seeded (PRODUCTION_SEED_DEFERRED).
    /// </summary>
    public IReadOnlyCollection<BcX509.X509Certificate> GetTrustedRoots()
        => _trustedRoots.AsReadOnly();

    /// <summary>
    /// Returns SHA-1 thumbprints (uppercase hex, no separators) of all trusted roots.
    /// </summary>
    public IReadOnlySet<string> GetTrustedRootThumbprints()
        => _trustedThumbprints;

    /// <summary>
    /// Computes the SHA-1 thumbprint of DER-encoded certificate bytes.
    /// Format: uppercase hex, no separators — e.g. "AB12CD34EF56...".
    /// </summary>
    public static string ComputeThumbprint(byte[] derBytes)
    {
        var hash = SHA1.HashData(derBytes);
        return Convert.ToHexString(hash); // uppercase, no separators — BCL guaranteed
    }
}
