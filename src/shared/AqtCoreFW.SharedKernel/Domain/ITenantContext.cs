namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>Ambient tenant context — resolved from JWT claims per request.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    bool IsSystemAdmin { get; }
}
