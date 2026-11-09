using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Typed HTTP client for the Viettel Mysign status-poll call (async=2 flow).
/// POST {BaseUrl}/vtss/service/requests/status
///
/// Called repeatedly by <see cref="ViettelMysignProvider"/> during InlinePoll mode,
/// or once by <see cref="ViettelMysignProvider.ConfirmAsync"/> for DeferredPoll.
///
/// <para>Polly resilience (3 attempts, exponential 200/600/1800 ms) is wired by
/// <c>InfrastructureRegistration</c> on this typed HttpClient — this class is unaware.
/// Status-poll is idempotent (read-only query), so retry is safe unlike SignHash.</para>
///
/// Response shape mirrors <see cref="MysignSignHashResponse"/>:
/// <c>{ status, signatures, transactionId, error, error_description }</c>.
/// </summary>
public sealed class ViettelMysignStatusPollClient(
    HttpClient http,
    IOptionsMonitor<ViettelMysignOptions> optionsMonitor,
    ILogger<ViettelMysignStatusPollClient> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Body sent to /requests/status — Viettel spec: { transactionId }
    private sealed record StatusPollRequest(
        [property: JsonPropertyName("transactionId")] string TransactionId);

    /// <summary>
    /// Queries the status of an async signing transaction by its Viettel-issued transaction ID.
    /// Returns the full response DTO including <c>status</c> code and (on success) <c>signatures</c>.
    /// Does NOT throw on application-level status codes — the caller (<see cref="ViettelMysignProvider"/>)
    /// interprets codes via <see cref="MysignStatusMapper"/>.
    /// </summary>
    /// <param name="transactionId">The transaction ID returned by signHash async=2 call.</param>
    /// <param name="accessToken">Bearer token from the Login step.</param>
    /// <param name="ct">Cancellation token — passes through to break a poll loop early.</param>
    /// <returns>The raw DTO from /requests/status.</returns>
    /// <exception cref="HttpRequestException">Network or HTTP 5xx/4xx error (after Polly exhaustion).</exception>
    /// <exception cref="System.Text.Json.JsonException">Unparseable response body.</exception>
    /// <exception cref="OperationCanceledException">Propagated when cancellation is requested.</exception>
    public async Task<MysignSignHashResponse> GetStatusAsync(
        string transactionId,
        string accessToken,
        CancellationToken ct)
    {
        var opts = optionsMonitor.CurrentValue;

        var body = new StatusPollRequest(transactionId);
        var statusPath = $"{opts.BaseUrl.TrimEnd('/')}/vtss/service/requests/status";

        using var request = new HttpRequestMessage(HttpMethod.Post, statusPath)
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        logger.LogDebug(
            "Viettel Mysign StatusPoll: querying status for transactionId={TransactionId}",
            transactionId);

        using var resp = await http.SendAsync(request, ct).ConfigureAwait(false);

        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Viettel Mysign StatusPoll HTTP {StatusCode} for transactionId={TransactionId}: {Body}",
                (int)resp.StatusCode, transactionId, raw);

            throw new HttpRequestException(
                $"Viettel Mysign /requests/status returned HTTP {(int)resp.StatusCode} for txId={transactionId}");
        }

        MysignSignHashResponse statusResp;
        try
        {
            statusResp = JsonSerializer.Deserialize<MysignSignHashResponse>(raw, JsonOpts)
                ?? throw new JsonException("Viettel Mysign StatusPoll: null/empty response body");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "Viettel Mysign StatusPoll: unparseable response for transactionId={TransactionId}: {Error}",
                transactionId, ex.Message);
            throw;
        }

        logger.LogDebug(
            "Viettel Mysign StatusPoll: transactionId={TransactionId} status={Status}",
            transactionId, statusResp.Status);

        return statusResp;
    }
}
