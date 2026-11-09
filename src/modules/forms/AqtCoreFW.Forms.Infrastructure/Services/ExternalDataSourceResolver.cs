using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Resolves data source options from an external HTTP API with distributed caching.
/// Gracefully returns empty list if API is unavailable.
/// Config JSON: { "ApiUrl": "https://...", "LabelJsonPath": "name", "ValueJsonPath": "id", "CacheSeconds": 300 }
/// H2 (authed external): optionally { "AuthMode": "Bearer" | "ApiKeyHeader", "VaultKeyId": "...", "HeaderName": "..." }
/// — the API key is read from Vault and attached as a request header. AuthMode null ⇒ the static path (backward).
/// </summary>
public sealed class ExternalDataSourceResolver(
    IHttpClientFactory httpClientFactory,
    IDistributedCache cache,
    ExternalDataSourceAuthKeyResolver authKeyResolver) : IDataSourceResolver
{
    public async Task<IReadOnlyList<DataSourceOptionDto>> ResolveAsync(
        string dataSourceJson, Guid tenantId,
        IReadOnlyDictionary<string, string>? parentValues = null,
        CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<ExternalDataSourceConfig>(dataSourceJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config is null || string.IsNullOrWhiteSpace(config.ApiUrl)) return [];

        // Cascade v1: append each parent value as a query param (UriBuilder-escaped, NOT string-concat —
        // injection/encoding surface). Host + scheme are UNCHANGED, so the SSRF guards below still cover it.
        var requestUrl = BuildRequestUrl(config.ApiUrl, parentValues);

        // SECURITY: Validate URL — HTTPS only, block RFC-1918 and link-local (SSRF mitigation)
        // F-23: Also resolve hostname and reject if resolved IP is private (DNS rebinding defence)
        if (!IsAllowedUrl(requestUrl)) return [];
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var resolvedUri)
            || await IsPrivateAfterResolutionAsync(resolvedUri)) return [];

        // H2: for an authed external source, resolve the Vault-stored API key up-front. If the config asks
        // for auth but no key is available (Vault unwired/miss/unreachable), FAIL CLOSED — do NOT fall back
        // to an unauthenticated request that would either 401 or, worse, hit an endpoint expecting the key.
        var isAuthed = !string.IsNullOrWhiteSpace(config.AuthMode);
        string? apiKey = null;
        if (isAuthed)
        {
            apiKey = await authKeyResolver.GetApiKeyAsync(config.VaultKeyId ?? "", cancellationToken);
            if (string.IsNullOrEmpty(apiKey)) return [];
        }

        // Cache-key MUST include the parent values, else a child gets another parent's cached list
        // (silent wrong-data). When parentValues is null/empty the suffix is empty ⇒ key byte-identical
        // to the pre-cascade path (no cache-miss storm on existing sources). The key is not affected by
        // auth (same URL+tenant+parents map to the same option-set regardless of the header).
        var cacheKey = BuildCacheKey(config.ApiUrl, tenantId, parentValues);

        // Try distributed cache first
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
            return JsonSerializer.Deserialize<List<DataSourceOptionDto>>(cached) ?? [];

        try
        {
            var client = httpClientFactory.CreateClient("FormsExternalDataSource");

            // Per-request message so the auth header is scoped to this call only (the named client is
            // shared; setting a default header would leak the key across every external source's requests).
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            if (isAuthed)
                ApplyAuthHeader(request, config.AuthMode!, config.HeaderName, apiKey!);

            using var httpResponse = await client.SendAsync(request, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();
            var response = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            var options = ExtractOptions(response, config.LabelJsonPath, config.ValueJsonPath);

            await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(options),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(config.CacheSeconds)
                },
                cancellationToken);

            return options;
        }
        catch (Exception)
        {
            // Graceful degradation — API down or misconfigured returns empty options
            return [];
        }
    }

    /// <summary>
    /// Attaches the Vault-resolved API key to the outbound request. <c>Bearer</c> sets the standard
    /// Authorization: Bearer header; <c>ApiKeyHeader</c> sets a custom header (default <c>Authorization</c>).
    /// Header values are never logged — the key stays inside the request object.
    /// </summary>
    private static void ApplyAuthHeader(HttpRequestMessage request, string authMode, string? headerName, string apiKey)
    {
        if (authMode.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return;
        }

        // ApiKeyHeader (or any other non-null mode): set a raw header. Default to Authorization when unset.
        var name = string.IsNullOrWhiteSpace(headerName) ? "Authorization" : headerName;
        request.Headers.TryAddWithoutValidation(name, apiKey);
    }

    /// <summary>
    /// Cascade v1: append each parent value as a query param to the configured ApiUrl using UriBuilder +
    /// parsed query (NEVER string concat — encoding + injection). Host/scheme are preserved so the SSRF
    /// guards still apply. null/empty parentValues ⇒ the URL is returned unchanged (backward path).
    /// Pure + static ⇒ unit-testable without HTTP. Params are appended in ordinal-key order for determinism.
    /// </summary>
    internal static string BuildRequestUrl(string apiUrl, IReadOnlyDictionary<string, string>? parentValues)
    {
        if (parentValues is null || parentValues.Count == 0)
            return apiUrl;

        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri))
            return apiUrl; // malformed — leave as-is; IsAllowedUrl will reject it downstream.

        // NOTE: do NOT use UriBuilder.Query setter — it round-trips through un-escape/re-escape and
        // mangles already-escaped values (e.g. a space is restored to a raw ' '). Build the query string
        // ourselves with Uri.EscapeDataString and concatenate onto the scheme/host/path/existing-query.
        var existingQuery = uri.GetComponents(UriComponents.Query, UriFormat.UriEscaped);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(existingQuery)) parts.Add(existingQuery);

        foreach (var kv in parentValues.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            parts.Add($"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");

        // Scheme + authority + path, WITHOUT the query (we append our own escaped query).
        var basePart = uri.GetComponents(
            UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path,
            UriFormat.UriEscaped);
        return $"{basePart}?{string.Join("&", parts)}";
    }

    /// <summary>
    /// Cache-key including parent values so a filtered child never returns another parent's cached list.
    /// null/empty parentValues ⇒ suffix is empty ⇒ key is byte-identical to the pre-cascade format
    /// <c>forms:ext-ds:{escapedUrl}:{tenantId}</c> (regression guard — no cache-miss storm). Deterministic
    /// ordinal-key ordering so the same parent set always maps to the same key.
    /// </summary>
    internal static string BuildCacheKey(
        string apiUrl, Guid tenantId, IReadOnlyDictionary<string, string>? parentValues)
    {
        var baseKey = $"forms:ext-ds:{Uri.EscapeDataString(apiUrl)}:{tenantId}";
        if (parentValues is null || parentValues.Count == 0)
            return baseKey;

        var suffix = string.Join("&", parentValues
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{baseKey}:{suffix}";
    }

    private static List<DataSourceOptionDto> ExtractOptions(
        string json, string labelPath, string valuePath)
    {
        var doc = JsonDocument.Parse(json);
        var results = new List<DataSourceOptionDto>();

        // Support simple property name or dot-notated last segment
        var labelProp = labelPath.Split('.').Last();
        var valueProp = valuePath.Split('.').Last();

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var label = item.TryGetProperty(labelProp, out var lp) ? lp.GetString() ?? "" : "";
                var value = item.TryGetProperty(valueProp, out var vp) ? vp.GetString() ?? "" : "";
                results.Add(new DataSourceOptionDto(value, label));
            }
        }

        return results;
    }

    /// <summary>
    /// SSRF mitigation: only HTTPS, block private/loopback/link-local IPs.
    /// Note: IP-literal checks here. Hostname-based DNS rebinding is caught by
    /// <see cref="IsPrivateAfterResolutionAsync"/> called separately in ResolveAsync.
    /// </summary>
    private static bool IsAllowedUrl(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)) return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return false;

        var host = uri.Host;
        // Block loopback
        if (host is "localhost" or "127.0.0.1" or "::1") return false;
        // Block link-local (169.254.x.x) and common private ranges via IP parsing
        if (IPAddress.TryParse(host, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                if (bytes[0] == 10) return false;                                    // 10.0.0.0/8
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false; // 172.16/12
                if (bytes[0] == 192 && bytes[1] == 168) return false;               // 192.168/16
                if (bytes[0] == 169 && bytes[1] == 254) return false;               // 169.254/16 (link-local)
            }
        }
        return true;
    }

    /// <summary>
    /// F-23: DNS rebinding SSRF defence — resolves the hostname and blocks if any resolved
    /// address is in a private/loopback range. Fails closed: unresolvable hosts are blocked.
    ///
    /// Performance note: This adds a DNS round-trip per uncached request. A domain allowlist
    /// is the preferred long-term solution for high-throughput environments.
    /// </summary>
    private static async Task<bool> IsPrivateAfterResolutionAsync(Uri uri)
    {
        // Skip for IP-literal hosts — already checked by IsAllowedUrl
        if (IPAddress.TryParse(uri.Host, out _)) return false;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host);
            return addresses.Any(IsPrivateIp);
        }
        catch
        {
            // Fail closed — block unresolvable hosts
            return true;
        }
    }

    /// <summary>
    /// Returns true if the IP address falls within a private/loopback/link-local range.
    /// M-01: Handles IPv4-mapped IPv6 (::ffff:x.x.x.x) and IPv6 link-local (fe80::/10).
    /// </summary>
    private static bool IsPrivateIp(IPAddress ip)
    {
        // M-01: Handle IPv4-mapped IPv6 addresses (e.g., ::ffff:127.0.0.1, ::ffff:10.0.0.1)
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return true;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            if (bytes[0] == 10) return true;                                    // 10.0.0.0/8
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true; // 172.16/12
            if (bytes[0] == 192 && bytes[1] == 168) return true;               // 192.168/16
            if (bytes[0] == 169 && bytes[1] == 254) return true;               // 169.254/16 (link-local)
        }

        // IPv6 link-local (fe80::/10)
        if (bytes.Length == 16 && bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
            return true;

        return false;
    }

    private sealed record ExternalDataSourceConfig(
        string ApiUrl,
        string LabelJsonPath,
        string ValueJsonPath,
        int CacheSeconds = 300,
        // Cascade v1: parent dependency descriptors (spike §1). The resolver receives already-resolved
        // parentValues from the caller and does NOT read this list at resolve time — it is declared here
        // only so the config shape stays the single source of truth for the DataSourceJson schema
        // (symmetry site #1). The list is actually consumed by save-time validation + the FE graph.
        List<CascadeDependency>? Dependencies = null,
        // H2 (authed external): null AuthMode ⇒ the static/unauthenticated path (backward — unchanged).
        // "Bearer" ⇒ Authorization: Bearer <key>; any other non-null value ⇒ raw header named by HeaderName.
        // VaultKeyId is the server-side data-source id used to read aqt/forms/external-ds/{VaultKeyId}/api-key
        // (never client-supplied — anti path-traversal). HeaderName defaults to Authorization when unset.
        string? AuthMode = null,
        string? VaultKeyId = null,
        string? HeaderName = null);

    private sealed record CascadeDependency(string DependsOnFieldKey, string FilterParamName);
}
