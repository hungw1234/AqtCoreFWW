using AqtCoreFW.Notifications.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Notifications.Domain.Entities;

/// <summary>
/// Notification template with Scriban rendering.
/// TemplateKey is unique per tenant + channel.
/// Templates are immutable per version — create new version instead of mutating.
///
/// T2 entity: AuditableEntity + IPublicIdentified (PublicId = v7 monotonic Guid AK).
/// TenantId: Guid (FK exception list — tenant isolation key).
/// </summary>
public sealed class NotificationTemplate : AuditableEntity, ITenantScoped, IPublicIdentified
{
    /// <summary>External addressing key — exposed in API URLs and DTOs. Set by factory via IGuidGenerator.</summary>
    public Guid PublicId { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Slug key — e.g. "case_submitted", "access_review_pending".</summary>
    public string TemplateKey { get; private set; } = string.Empty;

    public string SubjectTemplate { get; private set; } = string.Empty;

    /// <summary>Scriban body template. Max 20k chars. Regex timeout 100ms enforced at render.</summary>
    public string BodyTemplate { get; private set; } = string.Empty;

    public NotificationChannel Channel { get; private set; } = default!;

    public bool IsDefault { get; private set; }

    private NotificationTemplate() { } // EF Core

    public static NotificationTemplate Create(
        IGuidGenerator gen,
        Guid tenantId,
        string templateKey,
        string subjectTemplate,
        string bodyTemplate,
        NotificationChannel channel,
        Guid createdBy,
        bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyTemplate);

        var template = new NotificationTemplate
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            TemplateKey = templateKey.ToLowerInvariant(),
            SubjectTemplate = subjectTemplate,
            BodyTemplate = bodyTemplate,
            Channel = channel,
            IsDefault = isDefault
        };
        template.SetAuditCreate(createdBy);
        return template;
    }

    public void Update(string subjectTemplate, string bodyTemplate, Guid modifiedBy)
    {
        SubjectTemplate = subjectTemplate;
        BodyTemplate = bodyTemplate;
        SetAuditUpdate(modifiedBy);
    }

    public void ResetToDefault(string subjectTemplate, string bodyTemplate, Guid modifiedBy)
    {
        SubjectTemplate = subjectTemplate;
        BodyTemplate = bodyTemplate;
        IsDefault = true;
        SetAuditUpdate(modifiedBy);
    }
}
