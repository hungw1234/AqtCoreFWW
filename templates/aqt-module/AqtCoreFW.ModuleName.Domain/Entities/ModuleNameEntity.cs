using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.ModuleName.Domain.Entities;

public sealed class ModuleNameEntity : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    private ModuleNameEntity() { }

    public static ModuleNameEntity Create(
        Guid tenantId,
        string title,
        string? description,
        Guid createdBy)
    {
        var entity = new ModuleNameEntity
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
