namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Per-tenant control over how a server-side remote signature (Viettel Mysign PAdES) is driven.
///
/// <para><b>Async</b> (default) — the signing request submits the hash (Mysign <c>async=2</c>) and returns
/// immediately with a pollable id; a background job finalizes the PDF once the signer confirms the mobile OTP.
/// The request thread is never blocked, so it is safe behind gateways/proxies with short request timeouts. This
/// is the default because the transaction is persisted BEFORE the signature is relied upon — a partial failure
/// is recoverable without re-issuing signHash (no duplicate OTP).</para>
///
/// <para><b>Sync</b> — the signing request blocks (Mysign <c>async=0</c>, up to ~120s) until the signer confirms
/// on their phone, then assembles + stores the signed PDF in the same request. Simpler to reason about for an
/// internal/admin tool, but: (1) the caller must tolerate a long-running request (gateway/client timeouts), and
/// (2) a signature obtained then lost to a post-sign storage fault is orphaned (the signer must re-confirm).
/// Opt-in per tenant; never the default.</para>
///
/// <para><b>AsyncCallback</b> — the signing request submits the hash (Mysign <c>async=3</c>) and returns
/// immediately with a pending id; Mysign POSTs the result to our callback endpoint once the signer confirms,
/// and the transaction is finalized on <c>/mysign-callback/result</c>. Like <see cref="Async"/> it never blocks
/// the request thread, but it is push (vendor → us) instead of poll (us → vendor) — it requires a callback URL
/// that is publicly reachable from Viettel's network. Opt-in per tenant; never the default.</para>
/// </summary>
public enum SignatureFlowMode
{
    /// <summary>Async-poll (Mysign async=2) — return fast, finalize in a background job. Default.</summary>
    Async = 0,

    /// <summary>Synchronous block (Mysign async=0) — wait for the OTP and finalize in-request. Opt-in.</summary>
    Sync = 1,

    /// <summary>
    /// Async-callback (Mysign async=3) — return fast; Mysign POSTs the result to our callback endpoint,
    /// finalized on <c>/mysign-callback/result</c>. Requires a publicly-reachable callback URL. Opt-in.
    /// </summary>
    AsyncCallback = 2,
}
