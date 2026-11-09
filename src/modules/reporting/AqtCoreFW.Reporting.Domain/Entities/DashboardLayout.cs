using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Reporting.Domain.Entities;

/// <summary>
/// Dashboard layout aggregate — stores widget grid positions as JSON.
/// WidgetPositions format: [{"widgetId":"...", "x":0, "y":0, "w":6, "h":4}, ...]
/// One dashboard per owner can be flagged as IsDefault.
///
/// T1 entity: AuditableEntity (long Id). No PublicId (T1 = internal config only).
/// OwnerId: Guid (FK exception list — identity ref to AspNetUsers.Id).
/// TenantId: Guid (FK exception list — tenant isolation key).
/// </summary>
public sealed class DashboardLayout : AuditableEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public string Name { get; private set; } = string.Empty;
    /// <summary>Owner identity ref — Guid (FK exception list — AspNetUsers.Id).</summary>
    public Guid OwnerId { get; private set; }
    public bool IsDefault { get; private set; }

    /// <summary>JSON array of widget position descriptors: {widgetId, x, y, w, h}.</summary>
    public string WidgetPositions { get; private set; } = "[]";

    public Guid TenantId { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    private DashboardLayout() { } // EF Core

    /// <summary>Factory — creates a new DashboardLayout owned by a user within a tenant.</summary>
    public static DashboardLayout Create(
        string name,
        Guid ownerId,
        Guid tenantId,
        bool isDefault = false)
    {
        var layout = new DashboardLayout
        {
            Name = name,
            OwnerId = ownerId,
            TenantId = tenantId,
            IsDefault = isDefault,
            WidgetPositions = "[]"
        };
        layout.SetAuditCreate(ownerId);
        return layout;
    }

    /// <summary>Marks this layout as the default for its owner.</summary>
    public void SetAsDefault()
    {
        IsDefault = true;
        MarkUpdated();
    }

    /// <summary>Clears default flag (called when another layout becomes default).</summary>
    public void ClearDefault()
    {
        IsDefault = false;
        MarkUpdated();
    }

    /// <summary>Replaces the widget position grid JSON.</summary>
    public void UpdateLayout(string widgetPositionsJson)
    {
        WidgetPositions = widgetPositionsJson;
        MarkUpdated();
    }

    /// <summary>Updates the display name.</summary>
    public void Rename(string name)
    {
        Name = name;
        MarkUpdated();
    }
}
