namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>
/// Issues and validates short-lived inbound authentication tokens for Viettel Mysign callbacks, and
/// authenticates Mysign's callback client credentials.
///
/// <para>Mysign authenticates against BE by calling <c>POST /api/signing/mysign-callback/auth</c>
/// with client credentials (<see cref="AuthenticateCallbackAsync"/>). BE responds with a raw token
/// (base64 of 32 random bytes). Only the SHA-256 hash is persisted. Mysign presents the raw token as
/// Bearer in subsequent <c>POST /api/signing/mysign-callback/result</c> calls
/// (<see cref="TryValidateAsync"/>).</para>
///
/// <para>This is a transport-auth abstraction. It lives in Application (so a thin Presentation
/// controller can depend on it) while its implementation stays in Infrastructure (DB-backed token
/// store + bound options).</para>
/// </summary>
public interface IMysignInboundTokenService
{
    /// <summary>
    /// Generates a new 32-byte random token, persists the SHA-256 hash to DB, and returns
    /// the raw token (base64-encoded). The raw token is never stored.
    /// </summary>
    Task<string> IssueTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Validates an inbound Bearer token: hashes it, looks up the DB record, checks
    /// IsValid(now), updates LastUsedAt. Returns true if valid.
    /// </summary>
    Task<bool> TryValidateAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Authenticates Mysign's callback client credentials and, on success, issues an inbound token.
    /// Encapsulates the not-configured guard (503), the constant-time credential comparison (401), and
    /// the token TTL so the controller stays a thin transport mapper with no bound-options dependency.
    /// </summary>
    Task<MysignCallbackAuthOutcome> AuthenticateCallbackAsync(
        string? clientId, string? clientSecret, CancellationToken ct = default);
}

/// <summary>Result of <see cref="IMysignInboundTokenService.AuthenticateCallbackAsync"/>.</summary>
/// <param name="Status">Outcome the controller maps to an HTTP status.</param>
/// <param name="AccessToken">The issued raw token — non-null only when <see cref="MysignCallbackAuthStatus.Issued"/>.</param>
/// <param name="ExpiresInSeconds">Token lifetime — meaningful only when issued.</param>
public sealed record MysignCallbackAuthOutcome(
    MysignCallbackAuthStatus Status,
    string? AccessToken = null,
    int ExpiresInSeconds = 0);

/// <summary>Outcome of authenticating a Mysign callback handshake.</summary>
public enum MysignCallbackAuthStatus
{
    /// <summary>Callback inbound credentials are not configured on this deployment → 503.</summary>
    NotConfigured = 0,

    /// <summary>Presented credentials did not match → 401.</summary>
    InvalidCredentials = 1,

    /// <summary>Credentials matched; a token was issued → 200.</summary>
    Issued = 2,
}
