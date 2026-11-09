using System.Net;
using System.Net.Sockets;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Webhooks;

/// <summary>
/// SSRF prevention for outgoing webhook URLs (Security S260307 — CRITICAL).
/// Rules:
///   1. HTTPS only — reject http:// and any other scheme.
///   2. DNS-resolve the host and block all private/loopback/link-local IP ranges.
///   3. Called at subscription creation AND at delivery time (DNS rebinding guard).
/// Private ranges blocked: 10/8, 172.16/12, 192.168/16, 127/8, 169.254/16,
///                         ::1, fc00::/7 (fd00::/8 subset), link-local IPv6.
/// </summary>
public sealed class WebhookUrlValidator(ILogger<WebhookUrlValidator> logger)
{
    // IPv4 CIDR ranges that must never be webhook targets.
    private static readonly (uint Network, uint Mask)[] BlockedCidrsV4 =
    [
        (ToUint32("10.0.0.0"),     ToUint32("255.0.0.0")),       // RFC1918 10/8
        (ToUint32("172.16.0.0"),   ToUint32("255.240.0.0")),     // RFC1918 172.16/12
        (ToUint32("192.168.0.0"),  ToUint32("255.255.0.0")),     // RFC1918 192.168/16
        (ToUint32("127.0.0.0"),    ToUint32("255.0.0.0")),       // Loopback 127/8
        (ToUint32("169.254.0.0"),  ToUint32("255.255.0.0")),     // Link-local 169.254/16
        (ToUint32("100.64.0.0"),   ToUint32("255.192.0.0")),     // Shared address RFC6598
        (ToUint32("0.0.0.0"),      ToUint32("255.0.0.0")),       // "This" network
        (ToUint32("255.255.255.255"), ToUint32("255.255.255.255")), // Broadcast
    ];

    /// <summary>
    /// Validates the URL is safe to deliver a webhook to.
    /// Performs DNS resolution — do not call in tight loops without caching.
    /// Returns Fail with GOV_WHK_001 if blocked.
    /// </summary>
    public async Task<Result> ValidateAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Result.Fail(new Error("Invalid URL format.").WithMetadata("ErrorCode", "GOV_WHK_001"));

        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return Result.Fail(new Error("Webhook endpoint must use HTTPS.").WithMetadata("ErrorCode", "GOV_WHK_001"));

        // Reject explicit IP literals in the URL itself (fast path before DNS)
        if (IPAddress.TryParse(uri.Host, out var literalIp))
        {
            if (IsPrivateOrReserved(literalIp))
            {
                logger.LogWarning("Webhook SSRF blocked: literal IP {Ip} is private/reserved.", literalIp);
                return Result.Fail(new Error("Webhook endpoint resolves to a private/reserved address.")
                    .WithMetadata("ErrorCode", "GOV_WHK_001"));
            }
            return Result.Ok();
        }

        // DNS resolution — may return multiple addresses; ALL must be public
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        }
        catch (SocketException ex)
        {
            logger.LogWarning("Webhook URL DNS resolution failed for host {Host}: {Msg}", uri.Host, ex.Message);
            return Result.Fail(new Error("Webhook endpoint host could not be resolved.")
                .WithMetadata("ErrorCode", "GOV_WHK_001"));
        }

        if (addresses.Length == 0)
            return Result.Fail(new Error("Webhook endpoint host resolved to no addresses.")
                .WithMetadata("ErrorCode", "GOV_WHK_001"));

        foreach (var addr in addresses)
        {
            if (IsPrivateOrReserved(addr))
            {
                logger.LogWarning(
                    "Webhook SSRF blocked: host {Host} resolved to private/reserved IP {Ip}.",
                    uri.Host, addr);
                return Result.Fail(new Error("Webhook endpoint resolves to a private/reserved address.")
                    .WithMetadata("ErrorCode", "GOV_WHK_001"));
            }
        }

        return Result.Ok();
    }

    private static bool IsPrivateOrReserved(IPAddress addr)
    {
        // IPv6 checks
        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (addr.IsIPv6LinkLocal) return true;   // fe80::/10
            if (addr.Equals(IPAddress.IPv6Loopback)) return true; // ::1

            var bytes = addr.GetAddressBytes();
            // fc00::/7 — Unique Local Addresses (fd00::/8 is subset)
            if ((bytes[0] & 0xFE) == 0xFC) return true;
            // IPv4-mapped: ::ffff:0:0/96 — evaluate the embedded IPv4
            if (addr.IsIPv4MappedToIPv6)
                return IsPrivateOrReserved(addr.MapToIPv4());

            return false;
        }

        // IPv4 checks via CIDR table
        var raw = ToUint32(addr.ToString());
        foreach (var (network, mask) in BlockedCidrsV4)
        {
            if ((raw & mask) == network) return true;
        }
        return false;
    }

    private static uint ToUint32(string ipStr)
    {
        var parts = ipStr.Split('.');
        return ((uint)byte.Parse(parts[0]) << 24)
             | ((uint)byte.Parse(parts[1]) << 16)
             | ((uint)byte.Parse(parts[2]) << 8)
             |  (uint)byte.Parse(parts[3]);
    }

    private static uint ToUint32(IPAddress addr)
    {
        var b = addr.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }
}
