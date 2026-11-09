using System.Net;

namespace AqtCoreFW.Identity.Application.IpRules;

/// <summary>
/// Validates and parses CIDR notation for IPv4 and IPv6.
/// Uses System.Net.IPNetwork (.NET 8+) — no external NuGet required.
/// Examples: "192.168.1.0/24", "10.0.0.5/32", "2001:db8::/32".
/// </summary>
public static class CidrValidator
{
    /// <summary>Returns true if the string is a valid CIDR block parseable by IPNetwork.</summary>
    public static bool IsValidCidr(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr)) return false;
        return IPNetwork.TryParse(cidr.Trim(), out _);
    }

    /// <summary>
    /// Returns true if the CIDR is a "all networks" catch-all (prefix length = 0).
    /// Covers 0.0.0.0/0 (IPv4) and ::/0 (IPv6).
    /// Such rules would silently override every block rule and should never be stored.
    /// </summary>
    public static bool IsAllNetworkCidr(string cidr)
    {
        if (!IPNetwork.TryParse(cidr.Trim(), out var network)) return false;
        return network.PrefixLength == 0;
    }

    /// <summary>Rejects localhost/loopback CIDRs (127.0.0.0/8 and ::1/128) to prevent self-lockout.</summary>
    public static bool IsLocalhostCidr(string cidr)
    {
        if (!IPNetwork.TryParse(cidr.Trim(), out var network)) return false;

        // IPv4 loopback: 127.0.0.0/8
        if (IPNetwork.TryParse("127.0.0.0/8", out var ipv4Loop) && Overlaps(network, ipv4Loop))
            return true;

        // IPv6 loopback: ::1/128
        if (IPNetwork.TryParse("::1/128", out var ipv6Loop) && Overlaps(network, ipv6Loop))
            return true;

        return false;
    }

    /// <summary>Returns true if two networks overlap (either contains the other's base address).</summary>
    private static bool Overlaps(IPNetwork a, IPNetwork b)
        => a.Contains(b.BaseAddress) || b.Contains(a.BaseAddress);
}
