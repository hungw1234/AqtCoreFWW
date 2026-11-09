using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Files.Domain.Entities;

/// <summary>
/// A reusable document template using Scriban syntax for dynamic rendering.
/// Code is unique per tenant — used as stable identifier in integrations.
/// Status lifecycle: Draft → Active → Archived.
/// TemplateContent stores raw Scriban markup ({{ variable }} syntax).
/// T1: long PK (DB-assigned, via non-generic AuditableEntity). IAggregateRoot retains manual event list.
/// </summary>
public sealed class DocumentTemplate : AuditableEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DocumentOutputFormat OutputFormat { get; private set; }
    public string TemplateContent { get; private set; } = string.Empty;
    public DocumentTemplateStatus Status { get; private set; }

    /// <summary>TenantId — stays Guid (FK exception list).</summary>
    public Guid TenantId { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private DocumentTemplate() { } // EF Core

    public static DocumentTemplate Create(
        string name,
        string code,
        string? description,
        DocumentOutputFormat outputFormat,
        string templateContent,
        Guid tenantId,
        Guid createdBy)
    {
        var template = new DocumentTemplate
        {
            Name = name,
            Code = code,
            Description = description,
            OutputFormat = outputFormat,
            TemplateContent = templateContent,
            Status = DocumentTemplateStatus.Draft,
            TenantId = tenantId
        };
        template.SetAuditCreate(createdBy);
        return template;
    }

    /// <summary>Updates content and metadata while in Draft status.</summary>
    public void Update(string name, string? description, string templateContent, Guid modifiedBy)
    {
        Name = name;
        Description = description;
        TemplateContent = templateContent;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Publishes template — makes it available for document generation.</summary>
    public void Publish(Guid modifiedBy)
    {
        Status = DocumentTemplateStatus.Active;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Archives template — no longer available for new documents.</summary>
    public void Archive(Guid modifiedBy)
    {
        Status = DocumentTemplateStatus.Archived;
        SetAuditUpdate(modifiedBy);
    }

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public enum DocumentOutputFormat
{
    Pdf = 0,
    Docx = 1,
    Html = 2
}

public enum DocumentTemplateStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}
