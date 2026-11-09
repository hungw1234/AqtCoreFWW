using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// IP filter rule — global (TenantId null) or per-tenant deny/allow entry.
/// QĐ 742 §2.2.a: quản lý IP quản trị từ xa (remote admin IP whitelist/blocklist).
///
/// Tier: T1 — admin configuration entity, addressed by long Id.
/// Schema: [identity]
///
/// Semantics: IsBlock=true → block the CIDR range. IsBlock=false → allow (bypass block rules).
/// Middleware applies deny-first: a Block rule matching the IP returns 403.
/// An Allow rule matching IP bypasses all Block rules for that request.
///
/// CIDR format: "10.0.0.0/24" (IPv4), "2001:db8::/32" (IPv6), or single IP "192.168.1.5/32".
/// Middleware uses System.Net.IPNetwork for subnet matching — no external NuGet required (.NET 8+).
/// </summary>
public class IpFilterRule : AuditableEntity
{
    /// <summary>Null = global rule (all tenants); non-null = per-tenant rule.</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>CIDR notation, e.g. "192.168.1.0/24". Max 50 chars covers longest IPv6 CIDR.</summary>
    public string Cidr { get; private set; } = string.Empty;

    /// <summary>Human-readable description of why this rule exists.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>When false the rule is ignored by the middleware (soft disable without delete).</summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// True = BLOCK matching IPs (deny-list entry).
    /// False = ALLOW matching IPs (bypass all block rules — whitelist override).
    /// Default: true (blocklist entry, per existing middleware semantics).
    /// </summary>
    public bool IsBlock { get; private set; } = true;

    private IpFilterRule() { }

    public static IpFilterRule Create(
        Guid? tenantId,
        string cidr,
        string description,
        bool isEnabled,
        bool isBlock,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(cidr))
            throw new ArgumentException("Cidr must not be empty.", nameof(cidr));
        if (cidr.Length > 50)
            throw new ArgumentException("Cidr must not exceed 50 characters.", nameof(cidr));
        if (description.Length > 500)
            throw new ArgumentException("Description must not exceed 500 characters.", nameof(description));

        var rule = new IpFilterRule
        {
            TenantId    = tenantId,
            Cidr        = cidr.Trim(),
            Description = description.Trim(),
            IsEnabled   = isEnabled,
            IsBlock     = isBlock,
        };
        rule.SetAuditCreate(createdBy);
        return rule;
    }

    public void Update(
        string cidr,
        string description,
        bool isEnabled,
        bool isBlock,
        Guid modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(cidr))
            throw new ArgumentException("Cidr must not be empty.", nameof(cidr));
        if (cidr.Length > 50)
            throw new ArgumentException("Cidr must not exceed 50 characters.", nameof(cidr));
        if (description.Length > 500)
            throw new ArgumentException("Description must not exceed 500 characters.", nameof(description));

        Cidr        = cidr.Trim();
        Description = description.Trim();
        IsEnabled   = isEnabled;
        IsBlock     = isBlock;
        SetAuditUpdate(modifiedBy);
    }
}
