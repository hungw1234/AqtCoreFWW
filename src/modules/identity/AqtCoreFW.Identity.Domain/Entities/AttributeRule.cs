using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// ABAC attribute rule — admin-configurable without redeployment.
/// DepartmentAccessHandler queries these rules (cached 5 min).
/// T1: long PK (Id, DB-assigned IDENTITY).
/// </summary>
public class AttributeRule : Entity
{
    /// <summary>Resource being protected, e.g. "Case", "Document".</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>Action being authorized, e.g. "Read", "Submit", "Delete".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Claim key to match against user's claims, e.g. "department".</summary>
    public string AttributeKey { get; set; } = string.Empty;

    /// <summary>Required claim value for this rule to apply.</summary>
    public string AttributeValue { get; set; } = string.Empty;

    /// <summary>Allow or Deny.</summary>
    public AttributeEffect Effect { get; set; } = AttributeEffect.Allow;

    public Guid? TenantId { get; private set; }

    public static AttributeRule Create(string resource, string action, string attrKey, string attrValue, AttributeEffect effect, Guid? tenantId) =>
        new() { Resource = resource, Action = action, AttributeKey = attrKey, AttributeValue = attrValue, Effect = effect, TenantId = tenantId };
}

public enum AttributeEffect
{
    Allow = 1,
    Deny = 2
}
