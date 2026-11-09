namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>Marks entities that belong to a tenant (multi-tenancy via org_id column).</summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}
