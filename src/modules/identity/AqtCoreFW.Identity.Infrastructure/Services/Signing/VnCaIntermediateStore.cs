using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Immutable store of intermediate CA certificates used as PKIX path-building candidates.
///
/// <para>
/// <b>Candidate-only semantics:</b> Certificates in this store are added to the <c>allCerts</c>
/// candidate pool in <see cref="CertificateChainValidator"/> so PKIX can bridge a leaf
/// to the root even when the caller passes <c>intermediates: null</c> (raw-path mode).
/// They are deliberately excluded from <c>trustAnchors</c> and the pin-set — only root
/// certificates (held in <see cref="VnCaRootStore"/>) can act as trust anchors.
/// </para>
///
/// <para>
/// Empty-safe: constructed with no paths (deferred/unconfigured) behaves as an empty pool
/// without blocking startup.
/// </para>
/// </summary>
public sealed class VnCaIntermediateStore
{
    private readonly List<BcX509.X509Certificate> _candidates;

    /// <summary>
    /// Initialises the store by parsing DER or PEM encoded bytes into BouncyCastle certificates.
    /// Unparseable entries are silently skipped (callers should pre-validate via
    /// <see cref="TrustRootLoader"/> which already warn-logs bad files).
    /// </summary>
    /// <param name="derOrPemBytes">Raw bytes of DER or PEM intermediate CA certificates.</param>
    public VnCaIntermediateStore(IEnumerable<byte[]> derOrPemBytes)
    {
        _candidates = [];

        var parser = new BcX509.X509CertificateParser();
        foreach (var bytes in derOrPemBytes)
        {
            try
            {
                _candidates.Add(parser.ReadCertificate(bytes));
            }
            catch
            {
                // Unparseable bytes: skip silently — TrustRootLoader already logged a warning
                // when loading the file; double-logging here adds noise without value.
            }
        }
    }

    /// <summary>
    /// Returns all intermediate CA certificates as path-building candidates.
    /// Returns an empty collection when no intermediates are configured.
    /// </summary>
    public IReadOnlyCollection<BcX509.X509Certificate> GetCandidates()
        => _candidates.AsReadOnly();
}
