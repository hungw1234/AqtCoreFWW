using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Typed HTTP client for the Viettel Mysign SignHash call (sync mode, async=0).
/// POST {BaseUrl}/vtss/service/signHash
///
/// Sync mode: the request BLOCKS until the end-user confirms on their mobile app
/// (up to <see cref="ViettelMysignOptions.SyncSignTimeoutSeconds"/> seconds).
///
/// IMPORTANT: NO Polly retry is wired on this client. Retrying a sign request
/// that the user already confirmed (or rejected) would cause a duplicate signing
/// prompt. Idempotency cannot be assumed.
///
/// Hash algorithm OID (SHA-256): 2.16.840.1.101.3.4.2.1
/// RSA encryption OID:           1.2.840.113549.1.1.1
/// </summary>
public sealed class ViettelMysignSignHashClient(
    HttpClient http,
    IOptionsMonitor<ViettelMysignOptions> optionsMonitor,
    ILogger<ViettelMysignSignHashClient> logger)
{
    // SHA-256 OID per NIST FIPS 180-4
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    // RSA encryption OID per PKCS#1
    private const string RsaOid = "1.2.840.113549.1.1.1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Sends the pre-computed hash to Viettel Mysign for signing and returns raw RSA signature bytes.
    /// This is the W4 sync-mode API (async=0). It delegates to <see cref="SignHashRawAsync"/> and
    /// enforces the "signatures must be present" contract so callers receive a clean <c>byte[]</c>.
    /// </summary>
    /// <param name="userId">End-user identifier (used to prompt the correct mobile app user).</param>
    /// <param name="credentialId">credential_id from CertInfo — selects the signing key on Viettel side.</param>
    /// <param name="hashBytes">Pre-computed SHA-256 hash of the payload (32 bytes).</param>
    /// <param name="accessToken">Bearer token from Login step.</param>
    /// <param name="hashAlg">Hash algorithm identifier; defaults to SHA-256 OID.</param>
    /// <param name="signAlg">Asymmetric algorithm identifier; defaults to RSA OID.</param>
    /// <param name="documentName">Free-text reason shown on the signer's mobile app; sanitized to Viettel's charset.</param>
    /// <param name="ct">Cancellation token (should respect <see cref="ViettelMysignOptions.SyncSignTimeoutSeconds"/>).</param>
    /// <returns>Raw RSA PKCS#1 v1.5 signature bytes (NOT a CMS envelope).</returns>
    /// <exception cref="MysignSignHashException">Application-level error or empty signatures array.</exception>
    /// <exception cref="HttpRequestException">Network or HTTP 5xx error.</exception>
    /// <exception cref="TaskCanceledException">Timeout or caller cancellation.</exception>
    public async Task<byte[]> SignHashAsync(
        string userId,
        string credentialId,
        byte[] hashBytes,
        string accessToken,
        string? hashAlg = null,
        string? signAlg = null,
        string? documentName = null,
        CancellationToken ct = default)
    {
        var signResp = await SignHashRawAsync(
            userId:      userId,
            credentialId: credentialId,
            hashBytes:   hashBytes,
            accessToken: accessToken,
            asyncMode:   0,
            hashAlg:     hashAlg,
            signAlg:     signAlg,
            documentName: documentName,
            ct:          ct).ConfigureAwait(false);

        // Enforce sync-mode contract: signatures must be present in the response.
        if (!string.IsNullOrEmpty(signResp.Error))
            throw new MysignSignHashException(
                $"Viettel Mysign SignHash error [{signResp.Error}]: {signResp.ErrorDescription}");

        if (signResp.Signatures is null || signResp.Signatures.Count == 0)
            throw new MysignSignHashException(
                "Viettel Mysign SignHash: success response contained empty signatures array");

        try
        {
            return Convert.FromBase64String(signResp.Signatures[0]);
        }
        catch (FormatException ex)
        {
            throw new MysignSignHashException(
                $"Viettel Mysign SignHash: signatures[0] is not valid base64 — {ex.Message}");
        }
    }

    /// <summary>
    /// Low-level signHash call that returns the full <see cref="MysignSignHashResponse"/> DTO
    /// without interpreting application-level status codes. Used by async-poll modes (W5+).
    ///
    /// <para>For <paramref name="asyncMode"/>=0 the response contains <c>signatures</c> directly.
    /// For <paramref name="asyncMode"/>=2 the response contains only <c>transactionId</c>;
    /// caller must poll /requests/status via <see cref="ViettelMysignStatusPollClient"/>.</para>
    ///
    /// IMPORTANT: NO Polly retry is wired on the SignHash HTTP client — retrying would cause
    /// a duplicate mobile confirmation prompt. Idempotency cannot be assumed.
    /// </summary>
    /// <param name="asyncMode">Viettel spec <c>async</c> field: 0=sync, 2=async-poll, 3=async-callback.</param>
    /// <param name="documentName">Free-text reason shown on the signer's mobile app; sanitized to Viettel's charset.</param>
    /// <exception cref="MysignSignHashException">HTTP error response (non-2xx after parsing body).</exception>
    /// <exception cref="HttpRequestException">Network error.</exception>
    /// <exception cref="TaskCanceledException">Timeout or caller cancellation.</exception>
    public async Task<MysignSignHashResponse> SignHashRawAsync(
        string userId,
        string credentialId,
        byte[] hashBytes,
        string accessToken,
        int asyncMode = 0,
        string? hashAlg = null,
        string? signAlg = null,
        string? documentName = null,
        CancellationToken ct = default)
    {
        var opts = optionsMonitor.CurrentValue;

        // async=3 (callback) is useless without a callback URL — Mysign would have nowhere to POST the
        // result and the transaction would hang until TTL expiry. Fail loud rather than send a URL-less
        // request that looks accepted but never calls back.
        if (asyncMode == 3 && string.IsNullOrWhiteSpace(opts.CallbackUrl))
            throw new MysignSignHashException(
                "Viettel Mysign SignHash: async-callback mode (async=3) requires a configured CallbackUrl.");

        // One document → one hash → one signature. document_name is display-only (sanitized for Viettel's charset).
        // CallbackUrl is sent ONLY for async=3; for async 0/2 it stays null and WhenWritingNull drops it.
        var body = new MysignSignHashRequest(
            CredentialId:  credentialId,
            ClientId:      opts.ClientId,
            ClientSecret:  opts.ClientSecret,
            NumSignatures: 1,
            Documents:     [new MysignSignDocument(1, MysignDocumentName.Encode(documentName))],
            Hash:          [Convert.ToBase64String(hashBytes)],
            HashAlgo:      hashAlg ?? Sha256Oid,
            SignAlgo:      signAlg ?? RsaOid,
            Async:         asyncMode,
            CallbackUrl:   asyncMode == 3 ? opts.CallbackUrl : null);

        var signPath = $"{opts.BaseUrl.TrimEnd('/')}/vtss/service/signHash";

        using var request = new HttpRequestMessage(HttpMethod.Post, signPath)
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Apply sync timeout only for async=0 (blocks until mobile confirmation).
        // For async=2 the call returns quickly with a transactionId — no long timeout needed.
        CancellationTokenSource? timeoutCts = null;
        CancellationToken effectiveCt = ct;

        if (asyncMode == 0)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(
                opts.SyncSignTimeoutSeconds > 0 ? opts.SyncSignTimeoutSeconds : 120));
            effectiveCt = timeoutCts.Token;
        }

        logger.LogDebug(
            "Viettel Mysign SignHash: sending request for user {UserId} asyncMode={AsyncMode}",
            SigningLogMasking.MaskUserId(userId), asyncMode);

        try
        {
            using var resp = await http.SendAsync(request, effectiveCt).ConfigureAwait(false);

            var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Deserialize regardless of HTTP status — Viettel returns errors in body even on 4xx.
            MysignSignHashResponse? signResp = null;
            try
            {
                signResp = JsonSerializer.Deserialize<MysignSignHashResponse>(raw, JsonOpts);
            }
            catch (JsonException)
            {
                // If HTTP error + unparseable body, throw based on status below.
            }

            if (!resp.IsSuccessStatusCode)
            {
                var errMsg = signResp?.Error is not null
                    ? $"[{signResp.Error}] {signResp.ErrorDescription}"
                    : $"HTTP {(int)resp.StatusCode}";

                logger.LogWarning(
                    "Viettel Mysign SignHash HTTP {StatusCode} for user {UserId}: {Error}",
                    (int)resp.StatusCode, SigningLogMasking.MaskUserId(userId), errMsg);

                throw new MysignSignHashException(
                    $"Viettel Mysign SignHash failed — {errMsg}");
            }

            if (signResp is null)
                throw new MysignSignHashException(
                    "Viettel Mysign SignHash: empty or unparseable response body");

            return signResp;
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }
}
