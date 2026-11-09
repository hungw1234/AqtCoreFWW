using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Reporting.Domain.Entities;

/// <summary>
/// Dashboard widget aggregate — wraps a DataQuery executed via Dapper to produce chart/KPI data.
/// DataQuery MUST use parameterized SQL; execution is validated in the application layer.
///
/// T1 entity: AuditableEntity (long Id). No PublicId (T1 = internal config only).
/// TenantId: Guid (FK exception list — tenant isolation key).
/// CreatedBy: Guid (FK exception list — identity ref via SetAuditCreate).
/// </summary>
public sealed class ReportWidget : AuditableEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public string Name { get; private set; } = string.Empty;
    public WidgetType Type { get; private set; }

    /// <summary>Parameterized SQL executed by GetWidgetData handler (validated before execution).</summary>
    public string DataQuery { get; private set; } = string.Empty;

    /// <summary>Optional JSON config for rendering hints (e.g. color, axis labels).</summary>
    public string? Config { get; private set; }

    /// <summary>Client-side refresh interval in seconds (default 300 = 5 min).</summary>
    public int RefreshSeconds { get; private set; } = 300;

    public Guid TenantId { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    private ReportWidget() { } // EF Core

    /// <summary>Factory — creates a new ReportWidget for a tenant.</summary>
    public static ReportWidget Create(
        string name,
        WidgetType type,
        string dataQuery,
        Guid tenantId,
        Guid createdBy,
        string? config = null,
        int refreshSeconds = 300)
    {
        var widget = new ReportWidget
        {
            Name = name,
            Type = type,
            DataQuery = dataQuery,
            Config = config,
            RefreshSeconds = refreshSeconds,
            TenantId = tenantId
        };
        widget.SetAuditCreate(createdBy);
        return widget;
    }

    /// <summary>Updates the widget's data query. Caller must re-validate SQL before calling.</summary>
    public void UpdateQuery(string query)
    {
        DataQuery = query;
        MarkUpdated();
    }

    /// <summary>Updates the widget's JSON render config.</summary>
    public void UpdateConfig(string? json)
    {
        Config = json;
        MarkUpdated();
    }

    /// <summary>Updates name and optional refresh interval.</summary>
    public void UpdateDetails(string? name, int? refreshSeconds)
    {
        if (name is not null) Name = name;
        if (refreshSeconds.HasValue) RefreshSeconds = refreshSeconds.Value;
        MarkUpdated();
    }
}
