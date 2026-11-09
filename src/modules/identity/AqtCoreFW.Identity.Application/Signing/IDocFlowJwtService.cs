namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Issues and validates short-lived HS256 JWTs for the VGCA Tier-3 doc-flow handler.
///
/// <para>Claims payload: <c>{sub: userId, docId, nonce, tenant_id, exp: now+TTL, iss}</c></para>
/// <para>The same JWT is used for both the PDF download (slot 1) and signed-PDF upload (slot 2).
/// Replay prevention is enforced by the nonce table, not by the JWT itself; the <c>tenant_id</c>
/// claim is the signed source of truth the handler asserts against the record/nonce tenant before
/// any storage I/O (the round-trip is otherwise [AllowAnonymous] machine-to-machine).</para>
/// </summary>
public interface IDocFlowJwtService
{
    /// <summary>
    /// Issues a new HS256 JWT with the given claims. TTL is controlled by the host's bound
    /// <c>DocFlowJwtOptions.TtlSeconds</c> (default 300 s).
    /// </summary>
    string IssueJwt(Guid userId, Guid docId, Guid nonce, Guid tenantId);

    /// <summary>
    /// Validates the JWT and extracts the embedded claims.
    /// Returns <c>true</c> when the token is structurally valid, signature matches, and not expired.
    /// Returns <c>false</c> (and sets <paramref name="error"/>) on any validation failure.
    /// </summary>
    bool TryValidate(
        string jwt,
        out Guid userId,
        out Guid docId,
        out Guid nonce,
        out Guid tenantId,
        out string? error);
}
