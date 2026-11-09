using System.ComponentModel.DataAnnotations;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Configuration for Viettel Mysign remote signing provider.
/// Bound from config section <c>Signing:Providers:ViettelMysign</c>.
///
/// <para>3-call flow: Login → CertificateInfo → SignHash (sync or async poll).</para>
///
/// <para>Vault wiring: bind <see cref="ClientSecret"/> from
/// Vault path <c>aqt/signing/viettel-mysign/credentials</c> at startup.</para>
/// Dev: set via user-secrets or appsettings.Development.json (never commit).
/// </summary>
public sealed class ViettelMysignOptions
{
    public const string SectionName = "Signing:Providers:ViettelMysign";

    /// <summary>Base URL of the Viettel Mysign VTSS gateway (no trailing slash).</summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>OAuth2 client_id issued by Viettel Mysign portal.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 client_secret. MUST come from Vault or user-secrets — never hardcode.
    /// Vault KV path hint: <c>aqt/signing/viettel-mysign/credentials</c>.
    /// </summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Viettel profile_id identifying the signing package/policy.
    /// Included in Login and CertificateInfo requests.
    /// </summary>
    [Required]
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>
    /// Optional default user_id to use when <see cref="SignRequest.UserId"/> is not supplied.
    /// Leave null to require callers to always supply a user identifier.
    /// </summary>
    public string? DefaultUserId { get; set; }

    /// <summary>
    /// Leeway seconds subtracted from <c>expires_in</c> when caching login tokens.
    /// Prevents using an about-to-expire token. Default 60s.
    /// </summary>
    public int LoginTokenCacheLeewaySeconds { get; set; } = 60;

    /// <summary>
    /// TTL (seconds) for cached certificate info per user. Default 300s (5 minutes).
    /// </summary>
    public int CertInfoCacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// HTTP timeout (seconds) for the signHash call. The sync mode blocks until the user
    /// confirms on their mobile app — must be long enough for human interaction. Default 120s.
    /// </summary>
    public int SyncSignTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Max retry attempts for Login and CertificateInfo HTTP calls (Polly resilience pipeline).
    /// SignHash is NOT retried (idempotency risk). Default 3.
    /// </summary>
    public int HttpRetryMaxAttempts { get; set; } = 3;

    // ── Async-poll settings (W5) ──────────────────────────────────────────────

    /// <summary>
    /// Default sign mode applied when a <see cref="SignRequest"/> does not supply
    /// an <c>ExtraParameters["mysignMode"]</c> override.
    /// Default: <see cref="MysignSignMode.Sync"/> (preserves W4 behaviour).
    /// </summary>
    public MysignSignMode DefaultMode { get; set; } = MysignSignMode.Sync;

    /// <summary>
    /// Interval in milliseconds between successive /requests/status polls
    /// during <see cref="MysignSignMode.AsyncPollInline"/> mode. Default 3000 ms (3 s).
    /// Use a small value (e.g. 50–100 ms) in tests to keep wall-clock time low.
    /// </summary>
    public int PollIntervalMilliseconds { get; set; } = 3000;

    /// <summary>
    /// Maximum number of /requests/status poll attempts before giving up and returning
    /// a "Polling timeout" failure. At the default 3000 ms interval this is a ~6-minute ceiling.
    /// Default 120.
    /// </summary>
    public int PollMaxAttempts { get; set; } = 120;

    // ── Async-callback settings (W6) ─────────────────────────────────────────

    /// <summary>
    /// Absolute URL that Viettel Mysign POSTs the signing result to when a request uses async=3
    /// (AsyncCallback). Sent in the signHash body ONLY for async=3; absent for async 0/2.
    /// Vault KV path: <c>aqt/signing/viettel-mysign/callback-inbound</c> (callback_url field).
    ///
    /// <para>MUST be publicly reachable from Viettel's network (inbound vendor → us) — a localhost or
    /// internal-only URL means the callback never arrives and the transaction hangs until TTL expiry.
    /// Empty string → async=3 is unusable; <c>SignHashRawAsync</c> fails loud rather than sending no URL.</para>
    /// </summary>
    public string? CallbackUrl { get; set; }

    /// <summary>
    /// Client ID that Viettel Mysign presents when calling
    /// <c>POST /api/signing/mysign-callback/auth</c> to obtain an inbound token.
    /// Vault KV path: <c>aqt/signing/viettel-mysign/callback-inbound</c> (client_id field).
    /// Empty string in dev — callbacks will be rejected with 503 until configured.
    /// </summary>
    public string CallbackInboundClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret that Viettel Mysign presents alongside <see cref="CallbackInboundClientId"/>.
    /// Vault KV path: <c>aqt/signing/viettel-mysign/callback-inbound</c> (client_secret field).
    /// Compared using constant-time equality to prevent timing attacks.
    /// </summary>
    public string CallbackInboundClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Lifetime in seconds of the inbound token issued to Viettel Mysign by the /auth callback.
    /// Default 3600 (1 hour) — Mysign should call /result within this window.
    /// </summary>
    public int CallbackTokenTtlSeconds { get; set; } = 3600;

    /// <summary>
    /// Time-to-live in hours for a <c>SigningPendingTransaction</c> row.
    /// Transactions not resolved within this window are marked Expired by the cleanup job.
    /// Default 24 hours.
    /// </summary>
    public int PendingTxTtlHours { get; set; } = 24;

    /// <summary>
    /// Grace period in minutes added to <see cref="PendingTxTtlHours"/> before the cleanup
    /// job marks a transaction as Expired. Prevents cleanup racing an in-flight callback.
    /// Default 60 minutes.
    /// </summary>
    public int PendingTxCleanupGraceMinutes { get; set; } = 60;
}
