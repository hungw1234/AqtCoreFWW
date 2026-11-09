namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Outcome of a signer-binding check. Value object — carries no exception so callers can map a
/// failure to their own Result contract (e.g. GOV_FRM_022) instead of catching a throw.
/// </summary>
public sealed record SignerBindingVerdict(bool IsBound, string Reason)
{
    public static SignerBindingVerdict Bound(string how) => new(true, how);
    public static SignerBindingVerdict NotBound(string why) => new(false, why);
}

/// <summary>
/// Enforces the anti-impersonation invariant: a signature is only accepted when the signing
/// certificate provably belongs to the assigned signer.
///
/// <para>Binding key (fail-closed):
/// <list type="number">
///   <item>If the signer has an active cert enrollment → the signing cert's serial+issuer (or
///     thumbprint) MUST byte-match an enrolled value (forge-resistant).</item>
///   <item>Else → the national id (CCCD) extracted from the cert subject MUST match the signer's
///     stored <c>SigningNationalId</c> (bootstrap path before enrollment).</item>
///   <item>If neither key resolves → NOT bound. Never defaults to "allow".</item>
/// </list>
/// </para>
///
/// Implementations MUST NOT throw for a binding failure — they return a verdict so the
/// <c>Result.Fail</c> contract of the calling handler is preserved (anon + authed form paths).
/// </summary>
public interface ISignerBindingGuard
{
    Task<SignerBindingVerdict> VerifyAsync(
        CertificateInfo signingCert,
        Guid signerUserId,
        Guid tenantId,
        CancellationToken ct = default);
}
