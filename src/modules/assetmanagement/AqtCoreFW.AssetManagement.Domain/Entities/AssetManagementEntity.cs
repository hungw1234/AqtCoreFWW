using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.AssetManagement.Domain.Entities;

public sealed class AssetManagementEntity : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private AssetManagementEntity() { }

    public static AssetManagementEntity Create(
        Guid tenantId,
        string title,
        string? description,
        Guid createdBy)
    {
        var entity = new AssetManagementEntity
        {
            TenantId = tenantId,
            Title = title,
            Description = description,
        };
        entity.SetAuditCreate(createdBy);
        return entity;
    }

    public void Update(string title, string? description, Guid updatedBy)
    {
        Title = title;
        Description = description;
        SetAuditUpdate(updatedBy);
    }
}
