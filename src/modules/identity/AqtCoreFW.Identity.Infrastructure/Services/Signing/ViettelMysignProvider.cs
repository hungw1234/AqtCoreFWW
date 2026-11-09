using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// Viettel Mysign remote signing provider — supports sync (async=0), async inline-poll,
/// async deferred-poll (async=2), and async-callback (async=3, W6).
///
/// <para><b>Sign flow:</b></para>
/// <list type="number">
///   <item>Login  — POST /vtss/service/ras/v1/login  → bearer token (cached)</item>
///   <item>CertInfo — POST /vtss/service/certificates/info → signer cert + credential_id (cached)</item>
///   <item>SignHash  — POST /vtss/service/signHash (async=0|2|3)</item>
///   <item>(async=2 only) Poll — POST /vtss/service/requests/status until terminal or MaxAttempts</item>
///   <item>(async=3) Mysign calls back to BE; result persisted in <see cref="SigningPendingTransaction"/>.</item>
/// </list>
///
/// <para><b>Mode selection (precedence):</b> ExtraParameters["mysignMode"] (literal provider mode) →
/// ExtraParameters["signFlowMode"] (admin <see cref="SignatureFlowMode"/>, mapped via
/// <see cref="MysignFlowModeMap"/>) → <see cref="ViettelMysignOptions.DefaultMode"/>.</para>
///
/// <para><b>Async=3 correlation:</b> the pending tx is keyed on ExtraParameters injected by the handler:
/// ["tenantId"] (Guid) and ["userId"] (internal Guid — NOT the CCCD in <see cref="SignRequest.UserId"/>),
/// plus optional ["requestId"]/["documentId"]. If userId is absent, Guid.Empty is stored + a warning logged.</para>
///
/// <para><b>Signature format:</b> raw RSA PKCS#1 v1.5. VerifyAsync handles both Raw and Pkcs7.</para>
/// </summary>
public sealed class ViettelMysignProvider : IDigitalSignatureProvider
{
    private readonly ViettelMysignLoginClient _loginClient;
    private readonly ViettelMysignCertInfoClient _certInfoClient;
    private readonly ViettelMysignSignHashClient _signHashClient;
    private readonly ViettelMysignStatusPollClient _statusPollClient;
    private readonly ICertificateChainValidator _chainValidator;
    private readonly IOptionsMonitor<ViettelMysignOptions> _optionsMonitor;
    private readonly ILogger<ViettelMysignProvider> _logger;
    // W6: IdentityDbContext for persisting SigningPendingTransaction rows (async=3 mode).
    private readonly IdentityDbContext? _db;

    public ViettelMysignProvider(
        ViettelMysignLoginClient loginClient,
        ViettelMysignCertInfoClient certInfoClient,
        ViettelMysignSignHashClient signHashClient,
        ViettelMysignStatusPollClient statusPollClient,
        ICertificateChainValidator chainValidator,
        IOptionsMonitor<ViettelMysignOptions> optionsMonitor,
        ILogger<ViettelMysignProvider> logger,
        IdentityDbContext? db = null)
    {
        _loginClient      = loginClient;
        _certInfoClient   = certInfoClient;
        _signHashClient   = signHashClient;
        _statusPollClient = statusPollClient;
        _chainValidator   = chainValidator;
        _optionsMonitor   = optionsMonitor;
        _logger           = logger;
        _db               = db;
    }

    /// <inheritdoc/>
    public string ProviderKey => "remote-viettel-mysign";

    /// <inheritdoc/>
    public string DisplayName => "Viettel Mysign (Remote)";

    /// <inheritdoc/>
    /// <remarks>
    /// ServerSideSigning: server initiates the multi-call flow.
    /// RawSign: produces raw RSA PKCS#1 v1.5 bytes (no CMS envelope).
    /// BatchSigning intentionally omitted — each signHash requires a separate mobile confirmation.
    /// </remarks>
    public SignatureProviderCapabilities Capabilities =>
        SignatureProviderCapabilities.ServerSideSigning |
        SignatureProviderCapabilities.RawSign;

    /// <inheritdoc/>
    /// <remarks>
    /// Dispatches to sync (async=0), inline-poll, deferred-poll, or callback mode
    /// based on <see cref="MysignSignMode"/> resolved from request ExtraParameters or options default.
    /// Never throws — all exceptions are caught and returned as IsSuccess=false.
    /// Exception: <see cref="OperationCanceledException"/> when the caller's token is cancelled
    /// is deliberately re-thrown (callers MUST observe cancellation).
    /// </remarks>
    public async Task<SignResult> SignAsync(SignRequest request, CancellationToken cancellationToken = default)
    {
        var opts   = _optionsMonitor.CurrentValue;
        var userId = request.UserId ?? opts.DefaultUserId;

        if (string.IsNullOrEmpty(userId))
        {
            return new SignResult(false, null, null,
                "Viettel Mysign: UserId is required for signing (no DefaultUserId configured)");
        }

        // Resolve mode: per-request ExtraParameters override > options default.
        var mode = ResolveMode(request, opts);

        try
        {
            // ── Step 1: Login → bearer token ──────────────────────────────────
            string accessToken;
            try
            {
                accessToken = await _loginClient
                    .GetAccessTokenAsync(userId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MysignLoginException ex)
            {
                _logger.LogWarning(ex, "Viettel Mysign Sign: Login failed for user {UserId}", SigningLogMasking.MaskUserId(userId));
                return new SignResult(false, null, null, $"Login failed: {ex.Message}");
            }

            // ── Step 2: CertInfo → signing cert + credential_id ───────────────
            CertificateInfo certInfo;
            string credentialId;
            try
            {
                (certInfo, credentialId, _) = await _certInfoClient
                    .GetCertInfoAsync(userId, accessToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MysignCertInfoException ex)
            {
                _logger.LogWarning(ex, "Viettel Mysign Sign: CertInfo failed for user {UserId}", SigningLogMasking.MaskUserId(userId));
                return new SignResult(false, null, null, $"CertInfo failed: {ex.Message}");
            }

            // ── Step 3: Compute SHA-256 hash of payload ────────────────────────
            var hashBytes = SHA256.HashData(request.Payload);

            // ── Step 4: SignHash — mode determines the async field value ────────
            var asyncFlag = mode switch
            {
                MysignSignMode.Sync                => 0,
                MysignSignMode.AsyncPollInline      => 2,
                MysignSignMode.AsyncPollDeferred    => 2,
                MysignSignMode.AsyncCallback        => 3,
                _ => 0,
            };

            // Sync mode uses the legacy byte[] path to preserve W4 contract.
            if (mode == MysignSignMode.Sync)
            {
                byte[] signatureBytes;
                try
                {
                    signatureBytes = await _signHashClient
                        .SignHashAsync(
                            userId:       userId,
                            credentialId: credentialId,
                            hashBytes:    hashBytes,
                            accessToken:  accessToken,
                            hashAlg:      request.HashAlg,
                            signAlg:      request.Encode,
                            documentName: request.Reason,
                            ct:           cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (MysignSignHashException ex)
                {
                    _logger.LogWarning(ex, "Viettel Mysign Sign: SignHash failed for user {UserId}", SigningLogMasking.MaskUserId(userId));
                    return new SignResult(false, null, null, $"SignHash failed: {ex.Message}");
                }

                _logger.LogInformation(
                    "Viettel Mysign Sign: success (sync) for user {UserId}, cert serial {Serial}",
                    SigningLogMasking.MaskUserId(userId), certInfo.SerialNumber);

                return new SignResult(
                    IsSuccess:          true,
                    Signature:          signatureBytes,
                    SigningCertificate: certInfo,
                    FailureReason:      null,
                    TransactionId:      null,
                    IsPending:          false);
            }

            // Async modes (2 or 3) — use raw DTO path.
            MysignSignHashResponse signResp;
            try
            {
                signResp = await _signHashClient
                    .SignHashRawAsync(
                        userId:       userId,
                        credentialId: credentialId,
                        hashBytes:    hashBytes,
                        accessToken:  accessToken,
                        asyncMode:    asyncFlag,
                        hashAlg:      request.HashAlg,
                        signAlg:      request.Encode,
                        documentName: request.Reason,
                        ct:           cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MysignSignHashException ex)
            {
                _logger.LogWarning(ex, "Viettel Mysign Sign: SignHash (async) failed for user {UserId}", SigningLogMasking.MaskUserId(userId));
                return new SignResult(false, null, null, $"SignHash failed: {ex.Message}");
            }

            // Check application-level error in signHash response.
            if (!string.IsNullOrEmpty(signResp.Error))
            {
                return new SignResult(false, null, certInfo,
                    $"SignHash error [{signResp.Error}]: {signResp.ErrorDescription}");
            }

            return mode switch
            {
                MysignSignMode.AsyncPollInline =>
                    await PollUntilTerminalAsync(
                        signResp.TransactionId ?? string.Empty,
                        accessToken, certInfo, cancellationToken)
                    .ConfigureAwait(false),

                MysignSignMode.AsyncPollDeferred =>
                    BuildDeferredResult(signResp.TransactionId, certInfo),

                MysignSignMode.AsyncCallback =>
                    await PersistAndReturnCallbackPendingAsync(
                        signResp.TransactionId, request, certInfo, cancellationToken)
                    .ConfigureAwait(false),

                _ => throw new NotSupportedException($"Unsupported MysignSignMode: {mode}"),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled — propagate; do NOT swallow as IsSuccess=false.
            throw;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Viettel Mysign Sign: timeout waiting for mobile confirmation (user {UserId})", SigningLogMasking.MaskUserId(userId));
            return new SignResult(false, null, null,
                "Viettel Mysign sign timed out waiting for mobile confirmation");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Viettel Mysign Sign: network error for user {UserId}", SigningLogMasking.MaskUserId(userId));
            return new SignResult(false, null, null,
                $"Network error contacting Viettel Mysign: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Viettel Mysign Sign: unexpected error for user {UserId}", SigningLogMasking.MaskUserId(userId));
            return new SignResult(false, null, null, "Unexpected error during Viettel Mysign signing");
        }
    }

    /// <summary>
    /// Confirms the outcome of a previously deferred async signing transaction.
    ///
    /// <para><b>Mode-aware dispatch (W6):</b>
    /// <list type="bullet">
    ///   <item>DB lookup first — if a <see cref="SigningPendingTransaction"/> row exists:
    ///     Completed → success; Failed/Expired → failure; Initiated → still pending.</item>
    ///   <item>If no DB row: fall back to /requests/status poll (handles Sync/AsyncPoll modes).</item>
    /// </list>
    /// </para>
    ///
    /// <para>Used by <c>ConfirmSignatureCommandHandler</c> to serve the confirm endpoint (W5/W6).</para>
    /// </summary>
    /// <param name="transactionId">The transaction ID returned by <see cref="SignAsync"/>.</param>
    /// <param name="userId">The user who initiated the signing — needed for bearer token re-auth.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<SignResult> ConfirmAsync(
        string transactionId,
        string userId,
        CancellationToken ct)
    {
        // ── DB-first: check async-callback pending transaction table (W6) ─────
        if (_db is not null)
        {
            var pending = await _db.SigningPendingTransactions
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct)
                .ConfigureAwait(false);

            if (pending is not null)
            {
                return pending.Status switch
                {
                    PendingTransactionStatus.Completed =>
                        new SignResult(
                            IsSuccess:          true,
                            Signature:          pending.Signature,
                            SigningCertificate: null, // cert info not stored in pending tx
                            FailureReason:      null,
                            TransactionId:      transactionId,
                            IsPending:          false),

                    PendingTransactionStatus.Initiated =>
                        new SignResult(
                            IsSuccess:          true,
                            Signature:          null,
                            SigningCertificate: null,
                            FailureReason:      null,
                            TransactionId:      transactionId,
                            IsPending:          true),

                    PendingTransactionStatus.Failed or PendingTransactionStatus.Expired =>
                        new SignResult(
                            IsSuccess:          false,
                            Signature:          null,
                            SigningCertificate: null,
                            FailureReason:      pending.FailureReason ?? "Transaction failed or expired",
                            TransactionId:      transactionId,
                            IsPending:          false),

                    _ => new SignResult(false, null, null,
                        $"Unknown pending transaction status: {pending.Status}"),
                };
            }
        }

        // ── Fall back to polling path (Sync / AsyncPoll modes) ────────────────
        // Re-authenticate to get a current bearer token (cached by LoginClient).
        string accessToken;
        try
        {
            accessToken = await _loginClient
                .GetAccessTokenAsync(userId, ct)
                .ConfigureAwait(false);
        }
        catch (MysignLoginException ex)
        {
            _logger.LogWarning(ex,
                "Viettel Mysign Confirm: Login failed for user {UserId}", SigningLogMasking.MaskUserId(userId));
            return new SignResult(false, null, null, $"Login failed: {ex.Message}");
        }

        // Re-fetch cert info (IMemoryCache ensures this is cheap when still warm).
        CertificateInfo? certInfo = null;
        try
        {
            (certInfo, _, _) = await _certInfoClient
                .GetCertInfoAsync(userId, accessToken, ct)
                .ConfigureAwait(false);
        }
        catch (MysignCertInfoException ex)
        {
            _logger.LogWarning(ex,
                "Viettel Mysign Confirm: CertInfo failed for user {UserId}", SigningLogMasking.MaskUserId(userId));
            // Non-fatal for confirm — cert info is decorative; proceed without it.
        }

        MysignSignHashResponse statusResp;
        try
        {
            statusResp = await _statusPollClient
                .GetStatusAsync(transactionId, accessToken, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Viettel Mysign Confirm: status poll failed for txId={TransactionId}", transactionId);
            return new SignResult(false, null, certInfo,
                $"Status poll failed: {ex.Message}");
        }

        var info = MysignStatusMapper.Map(statusResp.Status);

        if (!info.IsTerminal)
        {
            // Still in-flight — return pending; caller should try again later.
            return new SignResult(
                IsSuccess:          true,
                Signature:          null,
                SigningCertificate: certInfo,
                FailureReason:      null,
                TransactionId:      transactionId,
                IsPending:          true);
        }

        if (info.IsSuccess && statusResp.Signatures is { Count: > 0 })
        {
            byte[] sigBytes;
            try
            {
                sigBytes = Convert.FromBase64String(statusResp.Signatures[0]);
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex,
                    "Viettel Mysign Confirm: signatures[0] invalid base64 for txId={TransactionId}",
                    transactionId);
                return new SignResult(false, null, certInfo,
                    "Confirm: signatures[0] is not valid base64");
            }

            _logger.LogInformation(
                "Viettel Mysign Confirm: success for txId={TransactionId}", transactionId);

            return new SignResult(
                IsSuccess:          true,
                Signature:          sigBytes,
                SigningCertificate: certInfo,
                FailureReason:      null,
                TransactionId:      transactionId,
                IsPending:          false);
        }

        _logger.LogWarning(
            "Viettel Mysign Confirm: terminal failure for txId={TransactionId} reason={Reason}",
            transactionId, info.ReadableMessage);

        return new SignResult(
            IsSuccess:          false,
            Signature:          null,
            SigningCertificate: certInfo,
            FailureReason:      info.ReadableMessage,
            TransactionId:      transactionId,
            IsPending:          false);
    }

    // ── Verify (unchanged from W4) ────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<VerifyResult> VerifyAsync(VerifyRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Payload is null || request.Payload.Length == 0)
            return Fail("Payload is empty");

        if (request.Signature is null || request.Signature.Length == 0)
            return Fail("Signature bytes are empty");

        return request.Format == SignatureFormat.Raw
            ? await VerifyRawAsync(request, cancellationToken).ConfigureAwait(false)
            : await VerifyCmsAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Polls /requests/status in a loop until a terminal status is received or PollMaxAttempts
    /// is exhausted. Cancellation token propagates naturally via Task.Delay.
    /// </summary>
    private async Task<SignResult> PollUntilTerminalAsync(
        string txId,
        string accessToken,
        CertificateInfo certInfo,
        CancellationToken ct)
    {
        var opts = _optionsMonitor.CurrentValue;

        if (string.IsNullOrEmpty(txId))
        {
            return new SignResult(false, null, certInfo,
                "Viettel Mysign AsyncPoll: signHash returned no transactionId");
        }

        _logger.LogDebug(
            "Viettel Mysign InlinePoll: starting poll for txId={TxId} maxAttempts={Max} interval={Interval}ms",
            txId, opts.PollMaxAttempts, opts.PollIntervalMilliseconds);

        for (int i = 0; i < opts.PollMaxAttempts; i++)
        {
            // Cancellation propagates here — TaskCanceledException / OperationCanceledException
            // is NOT caught in this loop; it bubbles up to SignAsync's outer catch.
            await Task.Delay(
                TimeSpan.FromMilliseconds(opts.PollIntervalMilliseconds), ct)
                .ConfigureAwait(false);

            MysignSignHashResponse pollResp;
            try
            {
                pollResp = await _statusPollClient
                    .GetStatusAsync(txId, accessToken, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Viettel Mysign InlinePoll: status call failed on attempt {Attempt} for txId={TxId}",
                    i + 1, txId);
                // Non-fatal — continue polling; Polly on the status-poll client will have
                // retried network errors already; this handles the exhausted case.
                continue;
            }

            var info = MysignStatusMapper.Map(pollResp.Status);

            _logger.LogDebug(
                "Viettel Mysign InlinePoll: attempt {Attempt}/{Max} txId={TxId} status={Status} terminal={Terminal}",
                i + 1, opts.PollMaxAttempts, txId, pollResp.Status, info.IsTerminal);

            if (!info.IsTerminal) continue;

            if (info.IsSuccess && pollResp.Signatures is { Count: > 0 })
            {
                byte[] sigBytes;
                try
                {
                    sigBytes = Convert.FromBase64String(pollResp.Signatures[0]);
                }
                catch (FormatException ex)
                {
                    return new SignResult(false, null, certInfo,
                        $"InlinePoll: signatures[0] invalid base64 — {ex.Message}");
                }

                _logger.LogInformation(
                    "Viettel Mysign InlinePoll: success for txId={TxId} after {Attempts} polls",
                    txId, i + 1);

                return new SignResult(
                    IsSuccess:          true,
                    Signature:          sigBytes,
                    SigningCertificate: certInfo,
                    FailureReason:      null,
                    TransactionId:      txId,
                    IsPending:          false);
            }

            // Terminal failure.
            _logger.LogWarning(
                "Viettel Mysign InlinePoll: terminal failure for txId={TxId} reason={Reason}",
                txId, info.ReadableMessage);

            return new SignResult(
                IsSuccess:          false,
                Signature:          null,
                SigningCertificate: certInfo,
                FailureReason:      info.ReadableMessage,
                TransactionId:      txId,
                IsPending:          false);
        }

        // Loop exhausted — no terminal status within allowed attempts.
        _logger.LogWarning(
            "Viettel Mysign InlinePoll: polling timeout after {Attempts} attempts for txId={TxId}",
            opts.PollMaxAttempts, txId);

        return new SignResult(
            IsSuccess:          false,
            Signature:          null,
            SigningCertificate: certInfo,
            FailureReason:      $"Polling timeout — max attempts ({opts.PollMaxAttempts}) reached without terminal status",
            TransactionId:      txId,
            IsPending:          false);
    }

    private static SignResult BuildDeferredResult(string? txId, CertificateInfo certInfo)
        => new(
            IsSuccess:          true,
            Signature:          null,
            SigningCertificate: certInfo,
            FailureReason:      null,
            TransactionId:      txId,
            IsPending:          true);

    /// <summary>
    /// Persists a <see cref="SigningPendingTransaction"/> row for async-callback (async=3) mode
    /// then returns IsPending=true result. Called only when <c>_db</c> is available.
    /// </summary>
    private async Task<SignResult> PersistAndReturnCallbackPendingAsync(
        string? txId,
        SignRequest request,
        CertificateInfo certInfo,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(txId))
        {
            _logger.LogWarning("Viettel Mysign AsyncCallback: signHash returned no transactionId");
            return new SignResult(false, null, certInfo,
                "Viettel Mysign AsyncCallback: signHash returned no transactionId");
        }

        var opts = _optionsMonitor.CurrentValue;

        // Resolve tenantId from ExtraParameters["tenantId"] — injected by SignPayloadCommandHandler.
        // Falls back to Guid.Empty if not provided (deferred callers that don't supply tenant context).
        Guid tenantId = Guid.Empty;
        if (request.ExtraParameters is not null &&
            request.ExtraParameters.TryGetValue("tenantId", out var tenantIdStr) &&
            Guid.TryParse(tenantIdStr, out var parsed))
        {
            tenantId = parsed;
        }

        // Resolve the internal user Guid from ExtraParameters["userId"] — injected by SignPayloadCommandHandler.
        // NOTE: request.UserId carries the signer's CCCD (national id) for the CA, NOT the internal Guid, so
        // parsing it as a Guid would always yield Guid.Empty (the W6 attribution bug). Fall back to Empty +
        // warn if absent — a callback tx with empty userId cannot be confirmed by a user-scoped path.
        Guid userGuid = Guid.Empty;
        if (request.ExtraParameters is not null &&
            request.ExtraParameters.TryGetValue("userId", out var userIdStr) &&
            Guid.TryParse(userIdStr, out var ug))
        {
            userGuid = ug;
        }
        else
        {
            _logger.LogWarning(
                "Viettel Mysign AsyncCallback: no parseable ExtraParameters[\"userId\"] for txId={TxId} — " +
                "pending tx attributed to Guid.Empty (cannot be confirmed by a user-scoped path)", txId);
        }

        // Resolve requestId from ExtraParameters — used for correlation.
        var requestId = request.ExtraParameters?.GetValueOrDefault("requestId") ?? txId;

        // documentId is optional
        var documentId = request.ExtraParameters?.GetValueOrDefault("documentId");

        var ttl = TimeSpan.FromHours(opts.PendingTxTtlHours);

        var pendingTx = SigningPendingTransaction.Initiate(
            transactionId: txId,
            tenantId:      tenantId,
            userId:        userGuid,
            requestId:     requestId,
            documentId:    documentId,
            ttl:           ttl);

        if (_db is not null)
        {
            try
            {
                _db.SigningPendingTransactions.Add(pendingTx);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Viettel Mysign AsyncCallback: pending transaction created for txId={TxId}, " +
                    "tenantId={TenantId}, expiresAt={ExpiresAt:u}",
                    txId, tenantId, pendingTx.ExpiresAt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log but don't fail — client still has txId to use for retries/polling.
                _logger.LogError(ex,
                    "Viettel Mysign AsyncCallback: failed to persist pending transaction txId={TxId}",
                    txId);
            }
        }

        return new SignResult(
            IsSuccess:          true,
            Signature:          null,
            SigningCertificate: certInfo,
            FailureReason:      null,
            TransactionId:      txId,
            IsPending:          true);
    }

    private static MysignSignMode ResolveMode(SignRequest request, ViettelMysignOptions opts)
    {
        // 1. Direct provider-mode override ("mysignMode") — advanced/test callers pick the literal mode.
        if (request.ExtraParameters is not null &&
            request.ExtraParameters.TryGetValue("mysignMode", out var raw) &&
            !string.IsNullOrEmpty(raw) &&
            Enum.TryParse<MysignSignMode>(raw, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        // 2. Admin-controlled flow ("signFlowMode") injected by SignPayloadCommandHandler from the tenant
        //    SignFlowMode. Mapped through the single MysignFlowModeMap so the generic handler and the PAdES
        //    initiator cannot drift to different async values for the same flow setting.
        if (request.ExtraParameters is not null &&
            request.ExtraParameters.TryGetValue("signFlowMode", out var flowRaw) &&
            Enum.TryParse<SignatureFlowMode>(flowRaw, ignoreCase: true, out var flow))
        {
            return MysignFlowModeMap.ToMysignSignMode(flow);
        }

        // 3. Provider default (preserves the generic sign path's existing behaviour when no mode is set).
        return opts.DefaultMode;
    }

    // ── Raw RSA verify ────────────────────────────────────────────────────────

    private async Task<VerifyResult> VerifyRawAsync(VerifyRequest request, CancellationToken ct)
    {
        if (request.SignerCertDer is null || request.SignerCertDer.Length == 0)
            return Fail("Raw signature verify requires SignerCertDer — Viettel raw signatures do not embed the certificate");

        // ── 1. Load certificate (net10-safe API) ──────────────────────────────
        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadCertificate(request.SignerCertDer);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Viettel Mysign VerifyRaw: could not load signer cert DER");
            return Fail($"Could not load signer certificate: {ex.Message}");
        }

        // ── 2. Verify raw RSA PKCS#1 v1.5 signature ──────────────────────────
        using (cert)
        {
            var rsa = cert.GetRSAPublicKey();
            if (rsa is null)
                return Fail("Signer certificate does not contain an RSA public key");

            var hashAlg = request.HashAlg.ToUpperInvariant() switch
            {
                "SHA384" => HashAlgorithmName.SHA384,
                "SHA512" => HashAlgorithmName.SHA512,
                _        => HashAlgorithmName.SHA256,
            };

            bool sigValid;
            try
            {
                var hash = hashAlg.Name switch
                {
                    "SHA384" => SHA384.HashData(request.Payload),
                    "SHA512" => SHA512.HashData(request.Payload),
                    _        => SHA256.HashData(request.Payload),
                };

                sigValid = rsa.VerifyHash(hash, request.Signature, hashAlg, RSASignaturePadding.Pkcs1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Viettel Mysign VerifyRaw: RSA verify threw");
                return Fail($"RSA signature verification error: {ex.Message}");
            }

            if (!sigValid)
                return Fail("RSA signature is invalid — payload may have been tampered");

            var certInfo = BuildCertificateInfo(cert, request.SignerCertDer);

            return await RunChainValidationAsync(certInfo, request.SignerCertDer, null,
                request.OcspMode, ct).ConfigureAwait(false);
        }
    }

    // ── CMS/PKCS#7 verify (backward-compatible path) ─────────────────────────

    private async Task<VerifyResult> VerifyCmsAsync(VerifyRequest request, CancellationToken ct)
    {
        Org.BouncyCastle.Cms.CmsSignedData cms;
        try
        {
            var processable = new CmsProcessableByteArray(request.Payload);
            cms = new Org.BouncyCastle.Cms.CmsSignedData(processable, request.Signature);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Viettel Mysign VerifyCms: failed to parse CMS bytes");
            return Fail($"CMS signature could not be parsed: {ex.Message}");
        }

        var signerInfos = cms.GetSignerInfos();
        var signers     = signerInfos.GetSigners();
        var certStore   = cms.GetCertificates();

        Org.BouncyCastle.X509.X509Certificate? signerCert = null;

        foreach (SignerInformation signer in signers)
        {
            var matchedCerts = certStore.EnumerateMatches(signer.SignerID);
            Org.BouncyCastle.X509.X509Certificate? candidate = null;
            foreach (Org.BouncyCastle.X509.X509Certificate c in matchedCerts)
            {
                candidate = c;
                break;
            }

            if (candidate is null) continue;

            bool valid;
            try { valid = signer.Verify(candidate); }
            catch (CmsException ex)
            {
                _logger.LogWarning(ex, "Viettel Mysign VerifyCms: CmsException during verify");
                return Fail($"CMS signature verification failed: {ex.Message}");
            }

            if (!valid)
                return Fail("CMS signature is invalid — payload may have been tampered");

            signerCert = candidate;
            break;
        }

        if (signerCert is null)
            return Fail("No valid signer with matching certificate found in CMS structure");

        byte[] certDer = signerCert.GetEncoded();
        var certInfo = new CertificateInfo(
            SerialNumber: signerCert.SerialNumber.ToString(16).ToUpperInvariant(),
            Subject:      signerCert.SubjectDN.ToString(),
            Issuer:       signerCert.IssuerDN.ToString(),
            NotBefore:    signerCert.NotBefore,
            NotAfter:     signerCert.NotAfter,
            Thumbprint:   VnCaRootStore.ComputeThumbprint(certDer),
            RawData:      certDer);

        var intermediates = new List<byte[]>();
        try
        {
            foreach (Org.BouncyCastle.X509.X509Certificate c in certStore.EnumerateMatches(null))
            {
                if (c.SerialNumber.Equals(signerCert.SerialNumber) &&
                    c.IssuerDN.Equivalent(signerCert.IssuerDN))
                    continue;
                intermediates.Add(c.GetEncoded());
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Viettel Mysign VerifyCms: could not extract intermediates — proceeding without");
        }

        return await RunChainValidationAsync(certInfo, certDer,
            intermediates.Count > 0 ? intermediates : null,
            request.OcspMode, ct).ConfigureAwait(false);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private async Task<VerifyResult> RunChainValidationAsync(
        CertificateInfo certInfo,
        byte[] certDer,
        IReadOnlyList<byte[]>? intermediates,
        OcspMode ocspMode,
        CancellationToken ct)
    {
        ChainValidationResult chainResult;
        try
        {
            chainResult = await _chainValidator
                .ValidateAsync(certDer, intermediates, ocspMode, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Viettel Mysign chain validator threw unexpectedly for cert subject={Subject}",
                certInfo.Subject);
            return new VerifyResult(
                IsValid: false,
                FailureReason: $"Chain validation failed unexpectedly: {ex.Message}",
                SigningCertificate: certInfo,
                ChainResult: null);
        }

        _logger.LogInformation(
            "Viettel Mysign verify complete: IsValid={IsValid}, Subject={Subject}, OcspChecked={OcspChecked}",
            chainResult.IsValid, certInfo.Subject, chainResult.OcspChecked);

        return new VerifyResult(
            IsValid:            chainResult.IsValid,
            FailureReason:      chainResult.IsValid ? null : chainResult.FailureReason,
            SigningCertificate: certInfo,
            ChainResult:        chainResult);
    }

    private static VerifyResult Fail(string reason)
        => new(IsValid: false, FailureReason: reason, SigningCertificate: null, ChainResult: null);

    private static CertificateInfo BuildCertificateInfo(X509Certificate2 cert, byte[] derBytes)
        => new(
            SerialNumber: cert.SerialNumber ?? string.Empty,
            Subject:      cert.Subject,
            Issuer:       cert.Issuer,
            NotBefore:    cert.NotBefore.ToUniversalTime(),
            NotAfter:     cert.NotAfter.ToUniversalTime(),
            Thumbprint:   cert.Thumbprint,
            RawData:      derBytes);
}
