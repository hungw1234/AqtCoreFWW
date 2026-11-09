using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AqtCoreFW.Identity.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;

/// <summary>
/// Typed HTTP client for the Viettel Mysign CertificateInfo call.
/// POST {BaseUrl}/vtss/service/certificates/info
///
/// Returns the first ENABLED certificate found for the given userId+profileId combination.
/// Certificate info is cached keyed by "mysign:cert:{userId}:{ProfileId}" for
/// <see cref="ViettelMysignOptions.CertInfoCacheTtlSeconds"/> seconds.
///
/// Polly retry is wired by InfrastructureRegistration on this typed HttpClient.
/// </summary>
public sealed class ViettelMysignCertInfoClient(
    HttpClient http,
    IOptionsMonitor<ViettelMysignOptions> optionsMonitor,
    IMemoryCache cache,
    ILogger<ViettelMysignCertInfoClient> logger)
{
    // GeneralizedTime format used by Viettel: "YYYYMMDDHHMMSSZ"
    private const string GeneralizedTimeFormat = "yyyyMMddHHmmssZ";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Returns the first enabled certificate info for <paramref name="userId"/>.
    /// Also returns the credential_id needed for SignHash.
    /// Result is cached per user+profile for <see cref="ViettelMysignOptions.CertInfoCacheTtlSeconds"/>.
    /// </summary>
    /// <param name="accessToken">Bearer token from <see cref="ViettelMysignLoginClient"/>.</param>
    /// <exception cref="MysignCertInfoException">No enabled cert found or API error.</exception>
    /// <exception cref="HttpRequestException">Network/HTTP error.</exception>
    public async Task<(CertificateInfo CertInfo, string CredentialId, byte[] SignerCertDer)>
        GetCertInfoAsync(string userId, string accessToken, CancellationToken ct)
    {
        var opts = optionsMonitor.CurrentValue;
        var cacheKey = $"mysign:cert:{userId}:{opts.ProfileId}";

        if (cache.TryGetValue<(CertificateInfo, string, byte[])>(cacheKey, out var cached))
            return cached;

        var body = new MysignCertInfoRequest(
            ClientId:  opts.ClientId,
            ProfileId: opts.ProfileId,
            UserId:    userId);

        var certPath = $"{opts.BaseUrl.TrimEnd('/')}/vtss/service/certificates/info";

        using var request = new HttpRequestMessage(HttpMethod.Post, certPath)
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await http.SendAsync(request, ct).ConfigureAwait(false);

        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Viettel Mysign CertInfo HTTP {StatusCode}: {Body}",
                (int)resp.StatusCode, raw);
            throw new MysignCertInfoException(
                $"Viettel Mysign CertInfo failed — HTTP {(int)resp.StatusCode}");
        }

        List<MysignCertInfoItem>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<MysignCertInfoItem>>(raw, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new MysignCertInfoException($"Could not parse CertInfo response: {ex.Message}");
        }

        if (items is null || items.Count == 0)
            throw new MysignCertInfoException("CertInfo response returned empty certificate list");

        // Pick first item with an application-level error check
        var first = items[0];
        if (!string.IsNullOrEmpty(first.Error))
            throw new MysignCertInfoException(
                $"Viettel Mysign CertInfo error [{first.Error}]: {first.ErrorDescription}");

        if (first.Cert is null || first.Cert.Certificates is null || first.Cert.Certificates.Count == 0)
            throw new MysignCertInfoException("CertInfo item has no certificate bytes");

        if (!string.Equals(first.Cert.Status, "valid", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Viettel Mysign CertInfo: certificate status is '{Status}' for user {UserId}",
                first.Cert.Status, SigningLogMasking.MaskUserId(userId));
            throw new MysignCertInfoException(
                $"Viettel Mysign certificate is not valid (status='{first.Cert.Status}')");
        }

        if (string.IsNullOrEmpty(first.CredentialId))
            throw new MysignCertInfoException("CertInfo item missing credential_id");

        // certificates[0] = signer cert DER (base64-encoded per Viettel spec)
        byte[] signerDer;
        try
        {
            signerDer = Convert.FromBase64String(first.Cert.Certificates[0]);
        }
        catch (FormatException ex)
        {
            throw new MysignCertInfoException($"CertInfo certificates[0] is not valid base64: {ex.Message}");
        }

        var certInfo = BuildCertificateInfo(first.Cert, signerDer);

        var result = (certInfo, first.CredentialId, signerDer);

        // Size is mandatory: the Api host's shared IMemoryCache is registered with a SizeLimit, and a
        // Set without an entry Size throws InvalidOperationException. One cert-info = one entry → Size 1.
        cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(opts.CertInfoCacheTtlSeconds, 30)),
            Size = 1,
        });

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CertificateInfo BuildCertificateInfo(MysignCertDetail cert, byte[] derBytes)
    {
        var notBefore = ParseGeneralizedTime(cert.ValidFrom);
        var notAfter  = ParseGeneralizedTime(cert.ValidTo);

        return new CertificateInfo(
            SerialNumber: cert.SerialNumber ?? string.Empty,
            Subject:      cert.SubjectDN   ?? string.Empty,
            Issuer:       cert.IssuerDN    ?? string.Empty,
            NotBefore:    notBefore,
            NotAfter:     notAfter,
            Thumbprint:   VnCaRootStore.ComputeThumbprint(derBytes),
            RawData:      derBytes);
    }

    /// <summary>
    /// Parses Viettel GeneralizedTime format "YYYYMMDDHHMMSSZ".
    /// Returns <see cref="DateTime.MinValue"/> on parse failure (non-blocking).
    /// </summary>
    private static DateTime ParseGeneralizedTime(string? value)
    {
        if (string.IsNullOrEmpty(value)) return DateTime.MinValue;

        if (DateTime.TryParseExact(value, GeneralizedTimeFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
            return dt;

        // Fallback: try ISO 8601 (defensive — spec says GeneralizedTime)
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var fallback))
            return fallback;

        return DateTime.MinValue;
    }
}
