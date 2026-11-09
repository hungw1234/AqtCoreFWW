using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// ViewDefinition aggregate root — tenant-scoped, named view configuration for an entity type.
/// Holds ordered columns and pre-configured filters.
/// IsDefault=true means this view is shown when no specific view is requested (one per EntityType per tenant).
/// PageSize defaults to 20; DefaultSortDir is "asc" or "desc".
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
/// </summary>
public sealed class ViewDefinition : AuditableEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<ViewColumn> _columns = [];
    private readonly List<ViewFilter> _filters = [];

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public ViewType Type { get; private set; }
    public string? DefaultSortField { get; private set; }
    public string? DefaultSortDir { get; private set; }
    public int PageSize { get; private set; } = 20;
    public bool IsDefault { get; private set; }

    public IReadOnlyList<ViewColumn> Columns => _columns.AsReadOnly();
    public IReadOnlyList<ViewFilter> Filters => _filters.AsReadOnly();

    private ViewDefinition() { } // EF Core

    /// <summary>Factory — creates a new ViewDefinition with no columns/filters yet.</summary>
    public static ViewDefinition Create(
        string name,
        string entityType,
        ViewType type,
        Guid tenantId,
        Guid createdBy,
        string? defaultSortField = null,
        string? defaultSortDir = null,
        int pageSize = 20,
        bool isDefault = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("EntityType cannot be empty.", nameof(entityType));
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be greater than zero.");
        if (defaultSortDir is not null && defaultSortDir != "asc" && defaultSortDir != "desc")
            throw new ArgumentException("DefaultSortDir must be 'asc' or 'desc'.", nameof(defaultSortDir));

        var view = new ViewDefinition
        {
            TenantId = tenantId,
            Name = name,
            EntityType = entityType,
            Type = type,
            DefaultSortField = defaultSortField,
            DefaultSortDir = defaultSortDir,
            PageSize = pageSize,
            IsDefault = isDefault
        };
        view.SetAuditCreate(createdBy);
        return view;
    }

    /// <summary>Marks this view as the default for its EntityType+Tenant combination.</summary>
    public void SetAsDefault(Guid updatedBy)
    {
        IsDefault = true;
        SetAuditUpdate(updatedBy);
    }

    /// <summary>Updates mutable view-level settings.</summary>
    public void UpdateInfo(
        string? name,
        string? defaultSortField,
        string? defaultSortDir,
        int? pageSize,
        bool? isDefault,
        Guid updatedBy)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be empty.", nameof(name));
            Name = name;
        }

        if (defaultSortField is not null)
            DefaultSortField = defaultSortField;

        if (defaultSortDir is not null)
        {
            if (defaultSortDir != "asc" && defaultSortDir != "desc")
                throw new ArgumentException("DefaultSortDir must be 'asc' or 'desc'.", nameof(defaultSortDir));
            DefaultSortDir = defaultSortDir;
        }

        if (pageSize.HasValue)
        {
            if (pageSize.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be greater than zero.");
            PageSize = pageSize.Value;
        }

        if (isDefault.HasValue)
            IsDefault = isDefault.Value;

        SetAuditUpdate(updatedBy);
    }

    /// <summary>Appends a pre-built ViewColumn to this view.</summary>
    public void AddColumn(ViewColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        _columns.Add(column);
    }

    /// <summary>Replaces all columns with a new set. Used by update view command.</summary>
    public void ReplaceColumns(IEnumerable<ViewColumn> newColumns)
    {
        _columns.Clear();
        foreach (var col in newColumns)
        {
            ArgumentNullException.ThrowIfNull(col);
            _columns.Add(col);
        }
    }

    /// <summary>Appends a pre-built ViewFilter to this view.</summary>
    public void AddFilter(ViewFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filters.Add(filter);
    }

    // IAggregateRoot — domain events (not persisted; outbox handles external events)
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
