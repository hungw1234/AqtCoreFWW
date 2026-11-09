namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Configuration options for loading VN CA trust store certificates at startup.
///
/// <para>
/// Two pools are kept strictly separate:
/// <list type="bullet">
///   <item><see cref="RootCertPaths"/> — loaded into <see cref="VnCaRootStore"/> as trust anchors
///   (participate in both PKIX path-building candidates AND pin-set verification).</item>
///   <item><see cref="IntermediateCertPaths"/> — loaded into <see cref="VnCaIntermediateStore"/> as
///   path-building candidates only. They are never added to trustAnchors or the pin-set,
///   preventing an intermediate from being accepted as a chain terminator.</item>
/// </list>
/// </para>
///
/// <para>
/// Bound from <c>appsettings.json</c> section <c>Signing:TrustStore</c>.
/// Empty arrays (default) preserve the deferred fallback: VnCaRootStore has 0 roots,
/// SigningHealthCheckService emits a startup warning.
/// </para>
/// </summary>
public sealed class VnCaTrustStoreOptions
{
    /// <summary>Configuration section name: <c>Signing:TrustStore</c>.</summary>
    public const string SectionName = "Signing:TrustStore";

    /// <summary>
    /// Absolute or ContentRoot-relative paths to DER or PEM root CA certificate files.
    /// These become trust anchors in <see cref="VnCaRootStore"/>.
    /// </summary>
    public List<string> RootCertPaths { get; set; } = [];

    /// <summary>
    /// Absolute or ContentRoot-relative paths to DER or PEM intermediate CA certificate files.
    /// These become path-building candidates in <see cref="VnCaIntermediateStore"/> (never anchors).
    /// </summary>
    public List<string> IntermediateCertPaths { get; set; } = [];
}
