using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Typed HTTP client responsible for the Viettel Mysign Login call.
/// POST {BaseUrl}/vtss/service/ras/v1/login
///
/// Token caching: IMemoryCache keyed by "mysign:tok:{ClientId}:{userId}".
/// TTL = expires_in (clamped to max 3600s) minus LeewaySeconds, ensuring
/// we never use an about-to-expire token across the 3-call flow.
///
/// Polly retry (3 attempts, exponential 200ms/600ms/1800ms) is wired by
/// InfrastructureRegistration on the named HttpClient — this class is unaware.
/// </summary>
public sealed class ViettelMysignLoginClient(
    HttpClient http,
    IOptionsMonitor<ViettelMysignOptions> optionsMonitor,
    IMemoryCache cache,
    ILogger<ViettelMysignLoginClient> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Viettel UAT returns expires_in as a JSON number; allow a stringified number too (defensive).
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // Maximum sane expires_in value — clamp anything larger (spec says "3600" but be defensive)
    private const int MaxTokenTtlSeconds = 3600;

    /// <summary>
    /// Returns a valid bearer token for <paramref name="userId"/>, refreshing via Login API when expired.
    /// </summary>
    /// <exception cref="MysignLoginException">Login API returned an application-level error.</exception>
    /// <exception cref="HttpRequestException">Network or HTTP error (caller may retry).</exception>
    public async Task<string> GetAccessTokenAsync(string userId, CancellationToken ct)
    {
        var opts = optionsMonitor.CurrentValue;
        var cacheKey = $"mysign:tok:{opts.ClientId}:{userId}";

        if (cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
            return cached;

        logger.LogDebug("Viettel Mysign: refreshing login token for user {UserId}", SigningLogMasking.MaskUserId(userId));

        var body = new MysignLoginRequest(
            ClientId:     opts.ClientId,
            ClientSecret: opts.ClientSecret,
            ProfileId:    opts.ProfileId,
            UserId:       userId);

        var loginPath = $"{opts.BaseUrl.TrimEnd('/')}/vtss/service/ras/v1/login";

        using var resp = await http
            .PostAsJsonAsync(loginPath, body, JsonOpts, ct)
            .ConfigureAwait(false);

        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Viettel Mysign Login HTTP {StatusCode}: {Body}",
                (int)resp.StatusCode, raw);
            throw new MysignLoginException(
                $"Viettel Mysign Login failed — HTTP {(int)resp.StatusCode}");
        }

        MysignLoginResponse loginResp;
        try
        {
            loginResp = JsonSerializer.Deserialize<MysignLoginResponse>(raw, JsonOpts)
                ?? throw new MysignLoginException("Login response was null/empty JSON");
        }
        catch (JsonException ex)
        {
            throw new MysignLoginException($"Could not parse Login response: {ex.Message}");
        }

        // Application-level error in 200-OK body
        if (!string.IsNullOrEmpty(loginResp.Error))
            throw new MysignLoginException(
                $"Viettel Mysign Login error [{loginResp.Error}]: {loginResp.ErrorDescription}");

        if (string.IsNullOrEmpty(loginResp.AccessToken))
            throw new MysignLoginException("Login response missing access_token");

        var ttlSeconds = ParseExpiresIn(loginResp.ExpiresIn, opts.LoginTokenCacheLeewaySeconds);

        // Size is mandatory: the Api host's shared IMemoryCache is registered with a SizeLimit, and a
        // Set without an entry Size throws InvalidOperationException. One token = one entry → Size 1.
        cache.Set(cacheKey, loginResp.AccessToken, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttlSeconds),
            Size = 1,
        });

        return loginResp.AccessToken;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int ParseExpiresIn(int? expiresIn, int leewaySeconds)
    {
        var raw = expiresIn is > 0 ? expiresIn.Value : MaxTokenTtlSeconds; // defensive default

        // Clamp to max sane value then subtract leeway
        var clamped = Math.Min(raw, MaxTokenTtlSeconds);
        var effective = clamped - leewaySeconds;

        // Never cache for 0 or negative (avoids immediate re-login every call)
        return Math.Max(effective, 30);
    }
}
